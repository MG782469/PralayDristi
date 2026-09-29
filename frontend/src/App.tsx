import { useCallback, useEffect, useState } from 'react'
import { Activity, AlertTriangle, BarChart3, Crosshair, Database, Droplets, FlaskConical, Map as MapIcon, Mountain, Satellite } from 'lucide-react'
import { get } from './api'
import { useHub } from './hub'
import SimulatePage from './pages/SimulatePage'
import ResultsPage from './pages/ResultsPage'
import NaturalDamPage from './pages/NaturalDamPage'
import InversePage from './pages/InversePage'
import ValidationPage from './pages/ValidationPage'
import LakeWatchPage from './pages/LakeWatchPage'
import AtlasPage from './pages/AtlasPage'
import DataPage from './pages/DataPage'

type Route = { page: string; id?: string }

function parseHash(): Route {
  const [page, id] = window.location.hash.replace(/^#\/?/, '').split('/')
  return { page: page || 'simulate', id }
}

export function go(page: string, id?: string) {
  window.location.hash = `/${page}${id ? '/' + id : ''}`
}

const nav = [
  { section: 'Model' },
  { page: 'simulate', label: 'Dam break', icon: Droplets },
  { page: 'natural', label: 'Natural dam / lake', icon: Mountain },
  { page: 'results', label: 'Results', icon: MapIcon },
  { section: 'Decide' },
  { page: 'inverse', label: 'Inverse query', icon: Crosshair },
  { page: 'lakes', label: 'Lake watch & alerts', icon: Satellite, badge: 'alerts' },
  { page: 'atlas', label: 'Dam risk atlas', icon: BarChart3 },
  { section: 'Trust' },
  { page: 'validation', label: 'Validation', icon: FlaskConical },
  { page: 'data', label: 'Data & system', icon: Database },
] as const

export default function App() {
  const [route, setRoute] = useState<Route>(parseHash())
  const [system, setSystem] = useState<any>(null)
  const [newAlerts, setNewAlerts] = useState(0)

  useEffect(() => {
    const onHash = () => setRoute(parseHash())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  useEffect(() => {
    get<any>('/api/system').then(setSystem).catch(() => setSystem(null))
    const t = setInterval(() => get<any>('/api/system').then(setSystem).catch(() => setSystem(null)), 30000)
    return () => clearInterval(t)
  }, [])

  useHub('alert', useCallback(() => setNewAlerts(n => n + 1), []))

  useEffect(() => {
    if (route.page === 'lakes') setNewAlerts(0)
  }, [route.page])

  let content
  switch (route.page) {
    case 'results': content = <ResultsPage runId={route.id} />; break
    case 'natural': content = <NaturalDamPage />; break
    case 'inverse': content = <InversePage runId={route.id} />; break
    case 'validation': content = <ValidationPage />; break
    case 'lakes': content = <LakeWatchPage />; break
    case 'atlas': content = <AtlasPage />; break
    case 'data': content = <DataPage system={system} />; break
    default: content = <SimulatePage />
  }

  return (
    <div className="app">
      <nav className="nav" aria-label="Main">
        <div className="brand">
          <img src="/favicon.svg" alt="" className="brand-mark" />
          <div>
            <div className="brand-name">PralayDrishti</div>
            <div className="brand-sub">Dam-break HADR decision support</div>
          </div>
        </div>
        {nav.map((n, i) =>
          'section' in n ? (
            <div key={i} className="nav-section">{n.section}</div>
          ) : (
            <button key={n.page} className={`nav-item ${route.page === n.page ? 'active' : ''}`} onClick={() => go(n.page)}
              aria-current={route.page === n.page ? 'page' : undefined}>
              <n.icon size={17} aria-hidden />
              <span>{n.label}</span>
              {'badge' in n && newAlerts > 0 && <span className="badge red">{newAlerts}</span>}
            </button>
          ),
        )}
        <div className="nav-footer">
          {system ? (
            <>
              <div><span className="dot" style={{ background: 'var(--ok)' }} />API online {system.offline && <span className="badge amber">air-gapped</span>}</div>
              <div style={{ marginTop: 4 }}>
                <Activity size={11} /> {system.runningRuns} running · {system.runs} runs
              </div>
              <div>Delft3D: {system.delft3d ? 'configured' : 'export only'}</div>
            </>
          ) : (
            <div><span className="dot" style={{ background: 'var(--danger)' }} />API offline <AlertTriangle size={11} /></div>
          )}
        </div>
      </nav>
      <main className="main">{content}</main>
    </div>
  )
}
