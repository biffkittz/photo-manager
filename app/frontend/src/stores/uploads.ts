import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError } from '@/api/http'
import { api } from '@/api/endpoints'
import { uploadFile } from '@/api/upload'
import type { UploadItemStatus } from '@/api/types'

/** Client-side limits mirror the server's (the server re-validates everything). */
export const MAX_UPLOAD_BYTES = 100 * 1024 * 1024
export const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'] as const
const ACCEPTED_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.gif', '.webp']
const CONCURRENCY = 3

export type UploadPhase =
  | 'waiting'
  | 'uploading'
  | 'processing'
  | 'done'
  | 'duplicate'
  | 'failed'
  | 'rejected'
  | 'cancelled'

export interface UploadItem {
  localId: string
  fileName: string
  size: number
  progress: number
  phase: UploadPhase
  photoId: string | null
  message: string | null
  description: string | null
  renditionVersion: number
}

/** Validates a file before upload; returns a user-facing reason, or null when acceptable. */
export function validateFile(file: Pick<File, 'name' | 'size' | 'type'>): string | null {
  const name = file.name.toLowerCase()
  const typeOk =
    (ACCEPTED_TYPES as readonly string[]).includes(file.type) ||
    ACCEPTED_EXTENSIONS.some((ext) => name.endsWith(ext))
  if (!typeOk) {
    return 'Only JPEG, PNG, GIF and WebP images can be uploaded.'
  }

  if (file.size === 0) {
    return 'The file is empty.'
  }

  return file.size > MAX_UPLOAD_BYTES ? 'The file is larger than 100 MB.' : null
}

/**
 * The upload queue. State is normalized (ordered ids + lookup) so progress updates touch one item; the File
 * objects and abort controllers live outside reactive state (they are large and not serializable).
 */
export const useUploadsStore = defineStore('uploads', () => {
  const batchId = ref<string>(crypto.randomUUID())
  const order = ref<string[]>([])
  const byId = ref<Record<string, UploadItem>>({})
  const files = new Map<string, File>()
  const controllers = new Map<string, AbortController>()

  const items = computed(() =>
    order.value.map((id) => byId.value[id]).filter((i): i is UploadItem => i !== undefined),
  )
  const activeCount = computed(() => items.value.filter((i) => i.phase === 'uploading').length)
  const hasProcessing = computed(() => items.value.some((i) => i.phase === 'processing'))
  const summary = computed(() => ({
    total: items.value.length,
    done: items.value.filter((i) => i.phase === 'done').length,
    failed: items.value.filter((i) => i.phase === 'failed' || i.phase === 'rejected').length,
    inFlight: items.value.filter(
      (i) => i.phase === 'waiting' || i.phase === 'uploading' || i.phase === 'processing',
    ).length,
  }))

  function enqueue(selected: Iterable<File>): void {
    for (const file of selected) {
      const localId = crypto.randomUUID()
      const rejection = validateFile(file)
      byId.value[localId] = {
        localId,
        fileName: file.name,
        size: file.size,
        progress: 0,
        phase: rejection ? 'rejected' : 'waiting',
        photoId: null,
        message: rejection,
        description: null,
        renditionVersion: 0,
      }
      order.value.push(localId)
      if (!rejection) {
        files.set(localId, file)
      }
    }

    pump()
  }

  /** Starts waiting uploads up to the concurrency limit. */
  function pump(): void {
    for (const item of items.value) {
      if (activeCount.value >= CONCURRENCY) {
        return
      }

      if (item.phase === 'waiting') {
        void start(item.localId)
      }
    }
  }

  async function start(localId: string): Promise<void> {
    const item = byId.value[localId]
    const file = files.get(localId)
    if (!item || !file) {
      return
    }

    const controller = new AbortController()
    controllers.set(localId, controller)
    item.phase = 'uploading'
    item.message = null
    try {
      const accepted = await uploadFile(
        file,
        batchId.value,
        (p) => (item.progress = p),
        controller.signal,
      )
      item.photoId = accepted.photoId
      item.progress = 1
      item.phase = 'processing'
      item.message = 'Encrypting and describing...'
      files.delete(localId)
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        item.phase = 'cancelled'
      } else if (error instanceof ApiError && error.status === 409) {
        item.phase = 'duplicate'
        item.message = error.message
        files.delete(localId)
      } else {
        item.phase = 'failed'
        item.message = error instanceof ApiError ? error.message : 'Upload failed.'
      }
    } finally {
      controllers.delete(localId)
      pump()
    }
  }

  function cancel(localId: string): void {
    const item = byId.value[localId]
    if (item?.phase === 'waiting') {
      item.phase = 'cancelled'
      files.delete(localId)
    }

    controllers.get(localId)?.abort()
  }

  /** Retries an item: re-uploads if the upload itself failed, or re-queues server-side processing. */
  async function retry(localId: string): Promise<void> {
    const item = byId.value[localId]
    if (!item) {
      return
    }

    if (item.photoId && item.phase === 'failed') {
      await api.retryUpload(item.photoId)
      item.phase = 'processing'
      item.message = 'Retrying...'
    } else if (files.has(localId)) {
      item.phase = 'waiting'
      item.progress = 0
      pump()
    }
  }

  /** Merges server-side ingest status (polled while anything is processing). */
  function applyServerStatus(statuses: UploadItemStatus[]): void {
    const byPhoto = new Map(statuses.map((s) => [s.photoId, s]))
    for (const item of items.value) {
      const status = item.photoId ? byPhoto.get(item.photoId) : undefined
      if (!status || item.phase !== 'processing') {
        continue
      }

      item.description = status.description
      item.renditionVersion = status.renditionVersion
      switch (status.status) {
        case 'Ready':
          item.phase = 'done'
          item.message = null
          break
        case 'Duplicate':
          item.phase = 'duplicate'
          item.message = status.statusDetail ?? 'Already in the library.'
          break
        case 'Failed':
          item.phase = 'failed'
          item.message = status.statusDetail ?? 'Processing failed.'
          break
        default:
          break
      }
    }
  }

  /** Aborts every transfer and forgets the queue (the session ended; nothing may continue under it). */
  function reset(): void {
    controllers.forEach((controller) => controller.abort())
    controllers.clear()
    files.clear()
    order.value = []
    byId.value = {}
    batchId.value = crypto.randomUUID()
  }

  function clearFinished(): void {
    const finished = new Set<UploadPhase>(['done', 'duplicate', 'rejected', 'cancelled'])
    order.value = order.value.filter((id) => {
      const keep = !finished.has(byId.value[id]?.phase ?? 'done')
      if (!keep) {
        Reflect.deleteProperty(byId.value, id)
      }

      return keep
    })
  }

  return {
    batchId,
    items,
    activeCount,
    hasProcessing,
    summary,
    enqueue,
    cancel,
    retry,
    applyServerStatus,
    clearFinished,
    reset,
  }
})
