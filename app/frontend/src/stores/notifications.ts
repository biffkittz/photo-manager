import { ref } from 'vue'
import { defineStore } from 'pinia'

export type NotificationKind = 'success' | 'error' | 'info'

export interface Notification {
  id: number
  kind: NotificationKind
  message: string
}

/** Toast notifications, announced to screen readers by ToastHost (aria-live). */
export const useNotificationsStore = defineStore('notifications', () => {
  const items = ref<Notification[]>([])
  let nextId = 1

  function notify(kind: NotificationKind, message: string, timeoutMs = 6000): number {
    const id = nextId++
    items.value.push({ id, kind, message })
    if (timeoutMs > 0) {
      setTimeout(() => dismiss(id), timeoutMs)
    }

    return id
  }

  function dismiss(id: number): void {
    items.value = items.value.filter((n) => n.id !== id)
  }

  return { items, notify, dismiss }
})
