import { LOGO_SIZE, logoRects } from '../lib/logo'

const base = { 'aria-hidden': true, fill: 'none', stroke: 'currentColor', strokeLinecap: 'round', strokeLinejoin: 'round' } as const

/**
 * The NitroDrp logo, a NOS bottle carrying an upload arrow, drawn from the same grid as the favicon and the phone
 * icons (src/assets/logo.json). `size` should be a multiple of 24 (24, 48) so every art pixel is a whole number of
 * screen pixels on common displays and the edges stay crisp.
 */
export const LogoMark = ({ size = 48 }: { size?: number }) => (
  <svg width={size} height={size} viewBox={`0 0 ${LOGO_SIZE} ${LOGO_SIZE}`} shapeRendering="crispEdges" aria-hidden="true">
    {logoRects().map((r) => (
      <rect key={`${r.x}-${r.y}`} x={r.x} y={r.y} width={r.w} height={1} fill={r.fill} />
    ))}
  </svg>
)
export const UploadIcon = ({ size = 40 }: { size?: number }) => (
  <svg {...base} width={size} height={size} viewBox="0 0 24 24" strokeWidth="1.6" stroke="#C6F432"><path d="M12 16V4M7 9l5-5 5 5" /><path d="M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3" /></svg>
)
export const FileIcon = () => (
  <svg {...base} width="24" height="24" viewBox="0 0 24 24" strokeWidth="1.6" style={{ flex: 'none', color: '#A3A3A3' }}><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" /><path d="M14 3v5h5" /></svg>
)
export const CheckIcon = ({ size = 16 }: { size?: number }) => (
  <svg {...base} width={size} height={size} viewBox="0 0 24 24" strokeWidth="2.4"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>
)
export const CloseIcon = () => (
  <svg {...base} width="18" height="18" viewBox="0 0 24 24" strokeWidth="2"><path d="M6 6l12 12M18 6 6 18" /></svg>
)
export const DownloadIcon = ({ size = 20 }: { size?: number }) => (
  <svg {...base} width={size} height={size} viewBox="0 0 24 24" strokeWidth="2"><path d="M12 4v12M7 11l5 5 5-5" /><path d="M5 20h14" /></svg>
)
