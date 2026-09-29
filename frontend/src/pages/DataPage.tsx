import { useEffect, useState } from 'react'
import { Database, Loader2, Plus, Trash2, Upload } from 'lucide-react'
import { Dam, del, get, post, upload } from '../api'
import { useAuth, useCanSimulate } from '../auth'

const emptyDam: Omit<Dam, 'id'> = {
  name: '', river: '', state: '', lat: 0, lon: 0, type: 'Earthfill', heightM: 30, grossStorageMcm: 10, crestLengthM: 300, crestWidthM: 10,
  downstreamKm: 40, category: 'Reservoir', approximate: true, notes: '', source: 'user', cascadeWith: [],
}

export default function DataPage({ system }: { system: any }) {
  const canSim = useCanSimulate()
  const isAdmin = useAuth().hasRole('Admin')
  const [dems, setDems] = useState<any[]>([])
  const [dams, setDams] = useState<Dam[]>([])
  const [form, setForm] = useState<Omit<Dam, 'id'>>(emptyDam)
  const [msg, setMsg] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = () => {
    get<any[]>('/api/dem').then(setDems)
    get<Dam[]>('/api/dams').then(setDams)
  }
  useEffect(load, [])

  const up = async (url: string, f: File) => {
    const fd = new FormData()
    fd.append('file', f)
    setBusy(true); setMsg(null)
    try {
      const r = await upload<any>(url, fd)
      setMsg(`Uploaded: ${JSON.stringify(r)}`)
      load()
    } catch (e: any) { setMsg(e.message) } finally { setBusy(false) }
  }

  const addDam = async () => {
    setMsg(null)
    try {
      await post('/api/dams', form)
      setForm(emptyDam)
      load()
      setMsg('Dam added to the register.')
    } catch (e: any) { setMsg(e.message) }
  }

  const f = (k: keyof typeof form, num = false) => ({
    value: (form[k] ?? '') as any,
    onChange: (e: any) => setForm({ ...form, [k]: num ? +e.target.value : e.target.value }),
  })

  return (
    <div className="page">
      <h1><Database size={18} /> Data & system</h1>
      <div className="grid-2">
        <div className="card stack">
          <h2>Digital elevation models</h2>
          <p className="faint">GeoTIFF in EPSG:4326: Copernicus GLO-30, FABDEM (best for floods), SRTM 30 m, ALOS AW3D30, ASTER GDEM, CartoDEM (Bhuvan).
            Projected rasters: <span className="mono">gdalwarp -t_srs EPSG:4326 in.tif out.tif</span>. Uploaded DEMs take priority over terrain tiles.</p>
          <label className="btn" style={{ alignSelf: 'flex-start' }} aria-disabled={!canSim}>
            {busy ? <Loader2 size={14} className="spin" /> : <Upload size={14} />} Upload DEM
            <input type="file" hidden disabled={!canSim} accept=".tif,.tiff" onChange={e => e.target.files?.[0] && up('/api/dem/upload', e.target.files[0])} />
          </label>
          <div className="table-wrap" style={{ maxHeight: 200 }}>
            <table>
              <thead><tr><th>File</th><th className="num">Size</th><th className="num">Cell</th><th>Extent</th></tr></thead>
              <tbody>{dems.map(d => (
                <tr key={d.file}><td>{d.file}</td><td className="num">{d.cols}×{d.rows}</td><td className="num">{(d.cellSize * 111000).toFixed(0)} m</td>
                  <td className="faint mono">{d.south.toFixed(2)}..{d.north.toFixed(2)}N {d.west.toFixed(2)}..{d.east.toFixed(2)}E</td></tr>
              ))}</tbody>
            </table>
          </div>
          {dems.length === 0 && <div className="faint">None uploaded - AWS terrain tiles (SRTM-based) are used and cached.</div>}
        </div>

        <div className="card stack">
          <h2>Exposure (settlements, facilities, buildings)</h2>
          <p className="faint">GeoJSON FeatureCollection. Points: <span className="mono">name, kind</span> (Village/Town/Hospital/School/Bridge/ArmyCamp...),
            <span className="mono"> population, deck_clearance</span>. Lines are roads. Polygons are building footprints (Google Open Buildings / Microsoft) and switch
            the loss model to per-building. OpenStreetMap is merged in automatically when online.</p>
          <label className="btn" style={{ alignSelf: 'flex-start' }} aria-disabled={!canSim}>
            <Upload size={14} /> Upload exposure GeoJSON
            <input type="file" hidden disabled={!canSim} accept=".json,.geojson" onChange={e => e.target.files?.[0] && up('/api/exposure/upload', e.target.files[0])} />
          </label>
        </div>
      </div>

      <div className="card stack">
        <h2>Dam register</h2>
        <div className="fields three">
          <div className="field"><label>Name</label><input type="text" {...f('name')} /></div>
          <div className="field"><label>River</label><input type="text" {...f('river')} /></div>
          <div className="field"><label>State</label><input type="text" {...f('state')} /></div>
          <div className="field"><label>Latitude</label><input type="number" step={0.0001} {...f('lat', true)} /></div>
          <div className="field"><label>Longitude</label><input type="number" step={0.0001} {...f('lon', true)} /></div>
          <div className="field"><label>Type</label>
            <select {...f('type')}>{['Earthfill', 'Rockfill', 'Concrete', 'LandslideDam', 'MoraineDam'].map(t => <option key={t}>{t}</option>)}</select></div>
          <div className="field"><label>Height (m)</label><input type="number" {...f('heightM', true)} /></div>
          <div className="field"><label>Gross storage (MCM)</label><input type="number" {...f('grossStorageMcm', true)} /></div>
          <div className="field"><label>Crest length (m)</label><input type="number" {...f('crestLengthM', true)} /></div>
          <div className="field"><label>Category</label>
            <select {...f('category')}>{['Reservoir', 'NaturalLake', 'Glacial', 'Transboundary'].map(t => <option key={t}>{t}</option>)}</select></div>
          <div className="field"><label>Downstream reach (km)</label><input type="number" {...f('downstreamKm', true)} /></div>
          <div className="field"><label>Source</label><input type="text" {...f('source')} /></div>
        </div>
        <button className="btn primary" style={{ alignSelf: 'flex-start' }} onClick={addDam} disabled={!isAdmin || !form.name || !form.lat || !form.lon} title={isAdmin ? undefined : 'Only admins edit the dam register'}><Plus size={14} /> Add dam</button>
        <div className="table-wrap">
          <table>
            <thead><tr><th>Name</th><th>River</th><th>State</th><th>Type</th><th className="num">Height</th><th className="num">MCM</th><th>Source</th><th /></tr></thead>
            <tbody>{dams.map(d => (
              <tr key={d.id}>
                <td>{d.name} {d.approximate && <span className="badge amber">approx.</span>}</td><td>{d.river}</td><td>{d.state}</td><td>{d.type}</td>
                <td className="num">{d.heightM}</td><td className="num">{d.grossStorageMcm}</td><td className="faint">{d.source}</td>
                <td><button className="btn small ghost" disabled={!isAdmin} aria-label={`Delete ${d.name}`} onClick={() => confirm(`Delete ${d.name}?`) && del(`/api/dams/${d.id}`).then(load)}><Trash2 size={12} /></button></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      </div>

      {msg && <div className="info-box">{msg}</div>}

      <div className="card">
        <h2>System</h2>
        <pre className="code">{JSON.stringify(system, null, 2)}</pre>
        <p className="faint">API docs: <a href="/swagger" target="_blank" rel="noreferrer">/swagger</a> · WMS: <a href="/wms?REQUEST=GetCapabilities" target="_blank" rel="noreferrer">/wms</a></p>
      </div>
    </div>
  )
}
