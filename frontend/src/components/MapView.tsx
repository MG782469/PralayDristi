import maplibregl, { GeoJSONSource, ImageSource, LayerSpecification, Map as MlMap, StyleSpecification } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { Globe, Layers, Mountain } from 'lucide-react'
import { ReactNode, useEffect, useRef, useState } from 'react'

export type Basemap = 'satellite' | 'streets' | 'terrain'

const baseStyle: StyleSpecification = {
  version: 8,
  glyphs: 'https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf',
  sources: {
    osm: {
      type: 'raster',
      tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
      tileSize: 256,
      maxzoom: 19,
      attribution: '&copy; OpenStreetMap contributors',
    },
    imagery: {
      type: 'raster',
      tiles: ['https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}'],
      tileSize: 256,
      maxzoom: 18,
      attribution: 'Imagery &copy; Esri, Maxar, Earthstar Geographics',
    },
    // served (and cached for offline use) by our own backend
    dem: {
      type: 'raster-dem',
      tiles: ['/api/tiles/terrarium/{z}/{x}/{y}.png'],
      encoding: 'terrarium',
      tileSize: 256,
      maxzoom: 13,
      attribution: 'Terrain: AWS Terrain Tiles (SRTM, GMTED, ETOPO)',
    },
  },
  layers: [
    { id: 'background', type: 'background', paint: { 'background-color': '#0b1220' } },
    { id: 'osm', type: 'raster', source: 'osm', layout: { visibility: 'none' }, paint: { 'raster-saturation': -0.3, 'raster-brightness-max': 0.85 } },
    { id: 'imagery', type: 'raster', source: 'imagery' },
    {
      id: 'hillshade',
      type: 'hillshade',
      source: 'dem',
      paint: { 'hillshade-shadow-color': '#0b1220', 'hillshade-exaggeration': 0.45, 'hillshade-highlight-color': '#cbd5e1' },
    },
  ],
}

type Props = {
  center?: [number, number]
  zoom?: number
  onReady?: (map: MlMap) => void
  onClick?: (lngLat: { lng: number; lat: number }) => void
  children?: ReactNode
  initialBasemap?: Basemap
}

export default function MapView({ center = [79.5, 29.0], zoom = 5, onReady, onClick, children, initialBasemap = 'satellite' }: Props) {
  const el = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MlMap | null>(null)
  const clickRef = useRef(onClick)
  const [basemap, setBasemap] = useState<Basemap>(initialBasemap)
  const [terrain3d, setTerrain3d] = useState(false)
  clickRef.current = onClick

  useEffect(() => {
    if (!el.current) return
    const map = new maplibregl.Map({
      container: el.current,
      style: baseStyle,
      center,
      zoom,
      maxPitch: 75,
      attributionControl: { compact: true },
    })
    map.addControl(new maplibregl.NavigationControl({ visualizePitch: true }), 'top-right')
    map.addControl(new maplibregl.ScaleControl({ unit: 'metric' }), 'bottom-right')
    map.on('click', e => clickRef.current?.(e.lngLat))
    map.on('load', () => onReady?.(map))
    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const apply = () => {
      map.setLayoutProperty('osm', 'visibility', basemap === 'streets' ? 'visible' : 'none')
      map.setLayoutProperty('imagery', 'visibility', basemap === 'satellite' ? 'visible' : 'none')
      map.setPaintProperty('hillshade', 'hillshade-exaggeration', basemap === 'terrain' ? 0.9 : 0.35)
    }
    if (map.isStyleLoaded()) apply()
    else map.once('load', apply)
  }, [basemap])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const apply = () => {
      if (terrain3d) {
        map.setTerrain({ source: 'dem', exaggeration: 1.4 })
        map.easeTo({ pitch: 60, duration: 800 })
      } else {
        map.setTerrain(null)
        map.easeTo({ pitch: 0, duration: 600 })
      }
    }
    if (map.isStyleLoaded()) apply()
    else map.once('load', apply)
  }, [terrain3d])

  return (
    <div className="map-wrap">
      <div ref={el} className="map" />
      <div className="map-toolbar">
        <div className="segmented" role="group" aria-label="Base map">
          <button className={basemap === 'satellite' ? 'on' : ''} onClick={() => setBasemap('satellite')} title="Satellite imagery">
            <Globe size={13} /> Satellite
          </button>
          <button className={basemap === 'streets' ? 'on' : ''} onClick={() => setBasemap('streets')} title="OpenStreetMap">
            <Layers size={13} /> Streets
          </button>
          <button className={basemap === 'terrain' ? 'on' : ''} onClick={() => setBasemap('terrain')} title="Hillshade only - works offline">
            <Mountain size={13} /> Terrain
          </button>
        </div>
        <button className={`btn small ${terrain3d ? 'accent' : ''}`} onClick={() => setTerrain3d(v => !v)} aria-pressed={terrain3d}>
          3D
        </button>
      </div>
      {children}
    </div>
  )
}

// ---------- helpers used by the pages ----------

export function setGeoJson(map: MlMap, id: string, data: GeoJSON.GeoJSON, layers: Omit<LayerSpecification, 'source'>[]) {
  const src = map.getSource(id) as GeoJSONSource | undefined
  if (src) src.setData(data as any)
  else map.addSource(id, { type: 'geojson', data: data as any })
  for (const l of layers) {
    if (!map.getLayer(l.id)) map.addLayer({ ...l, source: id } as LayerSpecification)
  }
}

export function removeSource(map: MlMap, id: string) {
  const style = map.getStyle()
  if (!style) return
  for (const l of style.layers) if ((l as any).source === id) map.removeLayer(l.id)
  if (map.getSource(id)) map.removeSource(id)
}

/** Image overlay (EPSG:4326 PNG from the backend) placed on the run's bounds. */
export function setImage(map: MlMap, id: string, url: string, b: [number, number, number, number], opacity = 0.85, before?: string) {
  const coords: [[number, number], [number, number], [number, number], [number, number]] = [
    [b[0], b[3]], [b[2], b[3]], [b[2], b[1]], [b[0], b[1]],
  ]
  const src = map.getSource(id) as ImageSource | undefined
  if (src) src.updateImage({ url, coordinates: coords })
  else {
    map.addSource(id, { type: 'image', url, coordinates: coords })
    map.addLayer({ id, type: 'raster', source: id, paint: { 'raster-opacity': opacity, 'raster-fade-duration': 0, 'raster-resampling': 'nearest' } },
      before && map.getLayer(before) ? before : undefined)
  }
  map.setPaintProperty(id, 'raster-opacity', opacity)
}

export function points(features: { lon: number; lat: number; props?: Record<string, unknown> }[]): GeoJSON.FeatureCollection {
  return {
    type: 'FeatureCollection',
    features: features.map(f => ({ type: 'Feature', geometry: { type: 'Point', coordinates: [f.lon, f.lat] }, properties: f.props ?? {} })),
  }
}

export function fitBounds(map: MlMap, b: [number, number, number, number], padding = 40) {
  map.fitBounds([[b[0], b[1]], [b[2], b[3]]], { padding, duration: 900 })
}
