import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { AuthResult, onSession, post, refresh, Role, setSession, User } from './api'
import { resetHub } from './hub'

type AuthState = {
  user: User | null
  /** true until the first silent refresh has answered, so we don't flash the login page on reload */
  restoring: boolean
  login: (email: string, password: string) => Promise<void>
  register: (email: string, password: string, fullName: string) => Promise<void>
  logout: () => Promise<void>
  hasRole: (...roles: Role[]) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [restoring, setRestoring] = useState(true)

  useEffect(() => onSession(r => setUser(r?.user ?? null)), [])

  useEffect(() => {
    refresh().finally(() => setRestoring(false))
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    setSession(await post<AuthResult>('/api/auth/login', { email, password }))
  }, [])

  const register = useCallback(async (email: string, password: string, fullName: string) => {
    setSession(await post<AuthResult>('/api/auth/register', { email, password, fullName }))
  }, [])

  const logout = useCallback(async () => {
    try { await post('/api/auth/logout') } catch { /* cookie is cleared server side either way */ }
    resetHub()
    setSession(null)
  }, [])

  const value = useMemo<AuthState>(() => ({
    user,
    restoring,
    login,
    register,
    logout,
    hasRole: (...roles) => !!user && roles.includes(user.role),
  }), [user, restoring, login, register, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth outside AuthProvider')
  return ctx
}

/** Run, simulate, upload and alert actions: Analyst and Admin only. */
export const useCanSimulate = () => useAuth().hasRole('Analyst', 'Admin')

export const viewerHint = 'Viewer accounts are read-only. Ask an admin for the Analyst role.'
