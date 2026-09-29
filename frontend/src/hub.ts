import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useEffect } from 'react'
import { getAccessToken, refresh } from './api'

let connection: HubConnection | null = null

function conn() {
  if (!connection) {
    connection = new HubConnectionBuilder()
      // the hub is [Authorize]; websockets can't carry headers so the token rides in ?access_token.
      // Called on every (re)connect, so a reconnect after expiry picks up a freshly refreshed token.
      .withUrl('/hubs/runs', { accessTokenFactory: async () => getAccessToken() ?? (await refresh())?.accessToken ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()
    connection.start().catch(err => console.warn('SignalR not connected, falling back to polling', err))
  }
  return connection
}

/** Drop the connection on logout so the next user connects with their own token. */
export function resetHub() {
  connection?.stop().catch(() => undefined)
  connection = null
}

/** Subscribe to a server push event for the lifetime of the component. */
export function useHub(event: string, handler: (...args: any[]) => void) {
  useEffect(() => {
    const c = conn()
    c.on(event, handler)
    return () => c.off(event, handler)
  }, [event, handler])
}
