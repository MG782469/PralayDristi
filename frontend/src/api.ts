// Thin fetch wrapper + the shapes the backend sends. Kept loose on purpose where the payload is
// only rendered (tables/charts) - no point mirroring every C# record here.

export type Dam = {
  id: number
  name: string
  river: string
  state: string
  lat: number
  lon: number
  type: string
  heightM: number
  grossStorageMcm: number
  crestLengthM: number
  crestWidthM: number
  downstreamKm?: number | null
  category: string
  approximate: boolean
  notes: string
  source: string
  cascadeWith: string[]
  dykes?: number[][] | null
}

export type KeyNumbers = {
  floodedAreaKm2: number
  populationAtRisk: number
  settlements: number
  firstArrivalMin: number | null
  peakDischarge: number
  maxDepth: number
  lossCrore: number
  bridgesOvertopped: number
  reachKm: number | null
  demSource: string
  syntheticDem: boolean
}

export type RunSummary = {
  id: string
  name: string
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
  progress: number
  stage: string
  createdUtc: string
  finishedUtc: string | null
  mode: string
  origin: string
  damId: number | null
  error: string | null
  numbers: KeyNumbers | null
}

export type DamSpec = {
  name: string
  lon: number
  lat: number
  type: string
  heightM: number
  waterDepthM?: number | null
  reservoirVolumeM3: number
  crestLengthM: number
  crestWidthM: number
  failureMode: string
  breachMethod?: string | null
  breachWidthM?: number | null
  formationTimeMin?: number | null
  storageExponent: number
}

export type ScenarioSpec = {
  name: string
  mode: 'DamBreak' | 'NaturalDam' | 'Cascade'
  dam: DamSpec
  naturalDam?: {
    debrisPolygon: number[][]
    crestElevation?: number | null
    heightAboveBed?: number | null
    currentLakeLevel?: number | null
    type: string
  } | null
  downstreamDams: DamSpec[]
  durationHours: number
  manning: number
  channelManning: number
  channelBurnM: number
  maxCells: number
  downstreamKm: number
  upstreamKm: number
  engine: 'Internal' | 'Delft3D'
  runSph: boolean
  sphHandover: boolean
  sphResolution: number
  debrisFlow: boolean
  structure?: { name: string; distanceKm: number; heightM: number; widthM: number } | null
  ensembleMembers: number
  ensembleSeed: number
  arrivalDepth: number
  warningMinutes: number
  demZoom: number
}

export type Place = {
  name: string
  kind: string
  lon: number
  lat: number
  population: number
  maxDepth: number
  maxSpeed: number
  arrivalMin: number | null
  peakMin: number | null
  hazardClass: number
  hazardLabel: string
  distanceFromDamKm: number
  evacuationMin: number | null
  goldenTimeMin: number | null
  urgency: string
  advice: string
}

export type Impact = {
  floodedAreaKm2: number
  maxDepth: number
  maxSpeed: number
  populationAtRisk: number
  settlementsAffected: number
  hazardAreaKm2: number[]
  places: Place[]
  bridges: { name: string; lon: number; lat: number; overtopped: boolean; maxWaterSurface: number; deckLevel: number; overtopMin: number | null; maxSpeed: number }[]
  roads: { name: string; category: string; floodedKm: number; firstCutMin: number | null }[]
  staging: { id: string; type: string; lon: number; lat: number; areaHa: number; meanSlope: number; elevation: number; distanceToFloodKm: number; nearRoad: boolean; score: number }[]
  sensors: { lon: number; lat: number; distanceFromDamKm: number; arrivalMin: number; leadTimes: { place: string; leadMin: number }[] }[]
  recommendations: string[]
  loss: { residentialInr: number; cropInr: number; roadsInr: number; bridgesInr: number; buildingsAffected: number; cropAreaHa: number; method: string; totalInr: number; totalCrore: number }
  floodFrontReachKm: number | null
}

export type Series = { t: number[]; v: number[] }

export type RunDetail = {
  summary: RunSummary
  spec: ScenarioSpec
  bounds: [number, number, number, number]
  dam: [number, number]
  breach: { method: string; averageWidth: number; bottomWidth: number; depth: number; sideSlope: number; formationTimeMin: number; empiricalPeak: number | null }
  empiricalPeaks: { method: string; peakM3s: number }[]
  hydrograph: Series
  routedHydrograph: Series | null
  reservoirLevel: Series | null
  impact: Impact
  gauges: { name: string; lon: number; lat: number; t: number[]; depth: number[]; speed: number[] }[]
  nearField: any | null
  ensemble: any | null
  cascade: { dam: string; failedAtMin: number | null; crestElevation: number }[]
  snapshotTimes: number[]
  model: any
  log: string[]
  naturalDam: any | null
  river: number[][]
  legends: Record<string, { item1: string; item2: string }[]>
}

async function handle<T>(r: Response): Promise<T> {
  if (!r.ok) {
    let msg = `${r.status} ${r.statusText}`
    try {
      const text = await r.text()
      if (text) {
        try {
          const j = JSON.parse(text)
          msg = j.title ? `${j.title}${j.errors ? ': ' + Object.values(j.errors).flat().join('; ') : ''}` : text
        } catch {
          msg = text.slice(0, 400)
        }
      }
    } catch { /* keep status text */ }
    throw new Error(msg)
  }
  const type = r.headers.get('content-type') ?? ''
  return (type.includes('json') ? r.json() : r.text()) as Promise<T>
}

// ---- auth plumbing ----------------------------------------------------------------------------
// The access token lives only in memory; the refresh token is an HttpOnly cookie scoped to /api/auth,
// so a page reload restores the session through refresh() without JS ever seeing the refresh token.

export type Role = 'Viewer' | 'Analyst' | 'Admin'
export type User = { id: string; email: string; fullName: string; role: Role; isActive: boolean; createdUtc: string; lastLoginUtc: string | null }
export type AuthResult = { accessToken: string; accessTokenExpiresUtc: string; refreshToken: string; refreshTokenExpiresUtc: string; user: User }

let accessToken: string | null = null
let refreshing: Promise<AuthResult | null> | null = null
const sessionListeners = new Set<(r: AuthResult | null) => void>()

export const getAccessToken = () => accessToken

/** Called by the auth context whenever the session starts, rotates or ends. */
export function onSession(fn: (r: AuthResult | null) => void) {
  sessionListeners.add(fn)
  return () => { sessionListeners.delete(fn) }
}

export function setSession(r: AuthResult | null) {
  accessToken = r?.accessToken ?? null
  sessionListeners.forEach(fn => fn(r))
}

/** Single-flight: ten requests failing with 401 at once still cause exactly one refresh call. */
export function refresh(): Promise<AuthResult | null> {
  if (!refreshing) {
    refreshing = fetch('/api/auth/refresh', { method: 'POST', credentials: 'include' })
      .then(r => (r.ok ? (r.json() as Promise<AuthResult>) : null))
      .catch(() => null)
      .then(r => { setSession(r); return r })
      .finally(() => { refreshing = null })
  }
  return refreshing
}

async function authFetch(url: string, init: RequestInit = {}, retry = true): Promise<Response> {
  const headers = new Headers(init.headers)
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  const r = await fetch(url, { ...init, headers, credentials: 'include' })
  if (r.status !== 401 || !retry || url.startsWith('/api/auth/')) return r
  return (await refresh()) ? authFetch(url, init, false) : r
}

export const get = <T,>(url: string) => authFetch(url).then(r => handle<T>(r))

const send = <T,>(method: string, url: string, body?: unknown) =>
  authFetch(url, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  }).then(r => handle<T>(r))

export const post = <T,>(url: string, body?: unknown) => send<T>('POST', url, body)
export const put = <T,>(url: string, body?: unknown) => send<T>('PUT', url, body)

export const del = (url: string) => authFetch(url, { method: 'DELETE' }).then(r => handle<unknown>(r))

export const upload = <T,>(url: string, form: FormData) => authFetch(url, { method: 'POST', body: form }).then(r => handle<T>(r))

/** Exports need the bearer header, so a plain <a href download> no longer works. */
export async function download(url: string) {
  const r = await authFetch(url)
  if (!r.ok) await handle(r)
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(r.headers.get('content-disposition') ?? '')?.[1] ?? url.split('/').pop()!
  const href = URL.createObjectURL(await r.blob())
  const a = Object.assign(document.createElement('a'), { href, download: decodeURIComponent(name) })
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(href), 10_000)
}

export const exportUrl = (runId: string, format: string) => `/api/runs/${runId}/export/${format}`
export const layerImage = (runId: string, layer: string) => `/api/runs/${runId}/image/${layer}.png`

// small formatting helpers used all over the place
export const fmt = {
  n: (v: number | null | undefined, d = 0) =>
    v === null || v === undefined || Number.isNaN(v) ? '-' : v.toLocaleString('en-IN', { maximumFractionDigits: d, minimumFractionDigits: d }),
  min: (v: number | null | undefined) => (v === null || v === undefined ? '-' : v < 90 ? `${v.toFixed(0)} min` : `${(v / 60).toFixed(1)} h`),
  mcm: (m3: number) => `${(m3 / 1e6).toLocaleString('en-IN', { maximumFractionDigits: 2 })} MCM`,
  time: (iso: string | null) => (iso ? new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleString('en-IN', { dateStyle: 'short', timeStyle: 'short' }) : '-'),
}
