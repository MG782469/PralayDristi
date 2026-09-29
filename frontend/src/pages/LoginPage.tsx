import { FormEvent, useState } from 'react'
import { Loader2, LogIn, ShieldCheck, UserPlus } from 'lucide-react'
import { useAuth } from '../auth'

// Seeded by appsettings.Development.json (Seed:DemoUsers). Override with VITE_DEMO_PASSWORD if you change it there.
const demoPassword = import.meta.env.VITE_DEMO_PASSWORD ?? 'Demo@12345'
const demos = [
  { role: 'Admin', email: 'admin@pralaydrishti.local', note: 'users, dam register, everything' },
  { role: 'Analyst', email: 'analyst@pralaydrishti.local', note: 'run simulations, raise alerts' },
  { role: 'Viewer', email: 'viewer@pralaydrishti.local', note: 'read-only' },
]

export default function LoginPage() {
  const { login, register } = useAuth()
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [fullName, setFullName] = useState('')
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const attempt = async (key: string, fn: () => Promise<void>) => {
    setBusy(key)
    setError(null)
    try { await fn() } catch (e: any) { setError(e.message) } finally { setBusy(null) }
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    attempt('form', () => (mode === 'login' ? login(email, password) : register(email, password, fullName)))
  }

  return (
    <div className="login-screen">
      <div className="card login-card stack">
        <div className="brand">
          <img src="/favicon.svg" alt="" className="brand-mark" />
          <div>
            <div className="brand-name">PralayDrishti</div>
            <div className="brand-sub">Dam-break HADR decision support</div>
          </div>
        </div>

        <form className="stack" onSubmit={submit}>
          <h2>{mode === 'login' ? 'Sign in' : 'Create an account'}</h2>
          {mode === 'register' && (
            <div className="field"><label htmlFor="fullName">Full name</label>
              <input id="fullName" type="text" autoComplete="name" required value={fullName} onChange={e => setFullName(e.target.value)} /></div>
          )}
          <div className="field"><label htmlFor="email">Email</label>
            <input id="email" type="text" inputMode="email" autoComplete="username" required value={email} onChange={e => setEmail(e.target.value)} /></div>
          <div className="field"><label htmlFor="password">Password</label>
            <input id="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required
              value={password} onChange={e => setPassword(e.target.value)} />
            {mode === 'register' && <span className="faint">At least 8 characters with a letter and a digit. New accounts start as Viewer.</span>}
          </div>
          {error && <div className="error-box" role="alert">{error}</div>}
          <button className="btn primary block" type="submit" disabled={busy !== null}>
            {busy === 'form' ? <Loader2 size={14} className="spin" /> : mode === 'login' ? <LogIn size={14} /> : <UserPlus size={14} />}
            {mode === 'login' ? 'Sign in' : 'Register'}
          </button>
          <button type="button" className="btn ghost small" onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); setError(null) }}>
            {mode === 'login' ? 'No account? Register' : 'Have an account? Sign in'}
          </button>
        </form>

        <div className="stack">
          <h3><ShieldCheck size={12} /> Demo accounts</h3>
          {demos.map(d => (
            <button key={d.role} type="button" className="btn block login-demo" disabled={busy !== null}
              onClick={() => attempt(d.role, () => login(d.email, demoPassword))}>
              {busy === d.role ? <Loader2 size={14} className="spin" /> : <LogIn size={14} />}
              <span><strong>{d.role}</strong> <span className="faint">{d.note}</span></span>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
