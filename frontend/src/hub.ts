import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useEffect } from 'react'

let connection: HubConnection | null = null

function conn() {
  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl('/hubs/runs')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()
    connection.start().catch(err => console.warn('SignalR not connected, falling back to polling', err))
  }
  return connection
}

/** Subscribe to a server push event for the lifetime of the component. */
export function useHub(event: string, handler: (...args: any[]) => void) {
  useEffect(() => {
    const c = conn()
    c.on(event, handler)
    return () => c.off(event, handler)
  }, [event, handler])
}
