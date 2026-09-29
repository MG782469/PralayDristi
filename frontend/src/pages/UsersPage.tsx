import { useCallback, useEffect, useState } from 'react'
import { Users } from 'lucide-react'
import { fmt, get, put, Role, User } from '../api'
import { useAuth } from '../auth'

const roles: Role[] = ['Viewer', 'Analyst', 'Admin']

export default function UsersPage() {
  const { user: me } = useAuth()
  const [users, setUsers] = useState<User[]>([])
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => get<User[]>('/api/users').then(setUsers).catch(e => setError(e.message)), [])
  useEffect(() => { load() }, [load])

  const change = (p: Promise<unknown>) => p.then(load).catch(e => setError(e.message))

  return (
    <div className="page">
      <h1><Users size={18} /> Users</h1>
      {error && <div className="error-box" role="alert">{error}</div>}
      <div className="card">
        <div className="table-wrap" style={{ maxHeight: 'none' }}>
          <table>
            <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Active</th><th>Last login</th><th>Created</th></tr></thead>
            <tbody>{users.map(u => {
              const self = u.id === me?.id
              return (
                <tr key={u.id}>
                  <td>{u.fullName}{self && <> <span className="badge">you</span></>}</td>
                  <td className="mono">{u.email}</td>
                  <td>
                    <select aria-label={`Role of ${u.email}`} value={u.role} disabled={self}
                      onChange={e => change(put(`/api/users/${u.id}/role`, { role: e.target.value }))}>
                      {roles.map(r => <option key={r}>{r}</option>)}
                    </select>
                  </td>
                  <td>
                    <input type="checkbox" aria-label={`${u.email} active`} checked={u.isActive} disabled={self}
                      onChange={e => change(put(`/api/users/${u.id}/active`, { active: e.target.checked }))} />
                  </td>
                  <td className="faint">{fmt.time(u.lastLoginUtc)}</td>
                  <td className="faint">{fmt.time(u.createdUtc)}</td>
                </tr>
              )
            })}</tbody>
          </table>
        </div>
        <p className="faint">You can't change your own role or disable yourself, so there is always at least one admin.</p>
      </div>
    </div>
  )
}
