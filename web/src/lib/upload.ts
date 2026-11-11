export interface UploadedPart {
  partNumber: number
  eTag: string
}

export interface PartUploadDeps {
  /** Ask the API for a fresh presigned PUT url for one part. */
  presign: (partNumber: number) => Promise<string>
  /** PUT the bytes, resolve with the ETag header. */
  putPart: (url: string, body: Blob, onProgress: (loaded: number) => void, signal?: AbortSignal) => Promise<string>
  onProgress: (uploadedBytes: number) => void
  signal?: AbortSignal
  concurrency?: number
  maxAttempts?: number
  sleep?: (ms: number) => Promise<void>
}

const defaultSleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms))

/**
 * Uploads a file as S3 multipart parts straight to the bucket: a few parts in flight at once,
 * each retried with backoff and a fresh presigned URL (so a stalled part can't outlive its URL).
 */
export async function uploadParts(
  file: Blob,
  partSize: number,
  partCount: number,
  deps: PartUploadDeps,
): Promise<UploadedPart[]> {
  const { concurrency = 4, maxAttempts = 5, sleep = defaultSleep, signal } = deps
  const loaded = new Array<number>(partCount).fill(0)
  const results: UploadedPart[] = []
  let next = 1

  const report = () => deps.onProgress(loaded.reduce((a, b) => a + b, 0))

  async function uploadOne(n: number) {
    const body = file.slice((n - 1) * partSize, Math.min(n * partSize, file.size))
    for (let attempt = 1; ; attempt++) {
      signal?.throwIfAborted()
      try {
        const url = await deps.presign(n)
        const eTag = await deps.putPart(
          url,
          body,
          (l) => {
            loaded[n - 1] = l
            report()
          },
          signal,
        )
        loaded[n - 1] = body.size
        report()
        results.push({ partNumber: n, eTag })
        return
      } catch (err) {
        loaded[n - 1] = 0
        report()
        if (signal?.aborted || attempt >= maxAttempts) throw err
        await sleep(Math.min(500 * 2 ** (attempt - 1), 8000))
      }
    }
  }

  async function worker() {
    while (next <= partCount) {
      signal?.throwIfAborted()
      await uploadOne(next++)
    }
  }

  await Promise.all(Array.from({ length: Math.min(concurrency, partCount) }, worker))
  return results.sort((a, b) => a.partNumber - b.partNumber)
}

/** Browser PUT with upload progress. fetch() can't report upload progress, XHR can. */
export function xhrPutPart(url: string, body: Blob, onProgress: (loaded: number) => void, signal?: AbortSignal): Promise<string> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', url)
    xhr.upload.onprogress = (e) => onProgress(e.loaded)
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        const etag = xhr.getResponseHeader('ETag')
        // Needs ExposeHeaders = ["ETag"] in the bucket's CORS rules (see infra/terraform).
        etag ? resolve(etag) : reject(new Error('Storage did not return an ETag. Check the bucket CORS settings.'))
      } else reject(new Error(`Upload failed (${xhr.status})`))
    }
    xhr.onerror = () => reject(new Error('Network error during upload'))
    xhr.onabort = () => reject(signal?.reason ?? new DOMException('Aborted', 'AbortError'))
    signal?.addEventListener('abort', () => xhr.abort(), { once: true })
    xhr.send(body)
  })
}
