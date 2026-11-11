import { demo } from './demoInstance'
import { xhrPutPart } from './upload'

/** Where part bytes go: S3 via presigned URL normally, a simulation in demo mode. */
export const putPart = demo ? demo.putPart : xhrPutPart
