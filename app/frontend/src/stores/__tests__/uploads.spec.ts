import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { UploadAccepted } from '@/api/types'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/api/http'

const pending: Array<{
  resolve: (v: { photoId: string; status: 'Queued' }) => void
  reject: (e: unknown) => void
}> = []
vi.mock('@/api/upload', () => ({
  uploadFile: vi.fn<() => Promise<UploadAccepted>>(
    () =>
      new Promise((resolve, reject) => {
        pending.push({ resolve, reject })
      }),
  ),
}))

const { useUploadsStore, validateFile, MAX_UPLOAD_BYTES } = await import('../uploads')
const { uploadFile } = await import('@/api/upload')

function file(name: string, size = 1000, type = 'image/jpeg'): File {
  const f = new File(['x'], name, { type })
  Object.defineProperty(f, 'size', { value: size })
  return f
}

describe('validateFile', () => {
  it.each([
    [{ name: 'a.jpg', size: 10, type: 'image/jpeg' }, null],
    [{ name: 'a.webp', size: 10, type: '' }, null],
    [
      { name: 'a.exe', size: 10, type: 'application/x-msdownload' },
      'Only JPEG, PNG, GIF and WebP images can be uploaded.',
    ],
    [{ name: 'a.jpg', size: 0, type: 'image/jpeg' }, 'The file is empty.'],
    [
      { name: 'a.jpg', size: MAX_UPLOAD_BYTES + 1, type: 'image/jpeg' },
      'The file is larger than 100 MB.',
    ],
  ])('validates %o', (input, expected) => {
    expect(validateFile(input)).toBe(expected)
  })
})

describe('uploads store', () => {
  beforeEach(() => {
    pending.length = 0
    setActivePinia(createPinia())
  })

  it('sends each file with the description choice it was queued with', async () => {
    const store = useUploadsStore()
    store.enqueue([file('quiet.jpg')], false)

    expect(uploadFile).toHaveBeenLastCalledWith(
      expect.any(File),
      store.batchId,
      expect.any(Function),
      expect.any(AbortSignal),
      false,
    )
    pending.shift()!.resolve({ photoId: 'p1', status: 'Queued' })
    await vi.waitFor(() => expect(store.items[0]!.phase).toBe('processing'))
    expect(store.items[0]!.message).toBe('Encrypting...')

    store.enqueue([file('described.jpg')])
    expect(uploadFile).toHaveBeenLastCalledWith(
      expect.any(File),
      store.batchId,
      expect.any(Function),
      expect.any(AbortSignal),
      true,
    )
  })

  it('reset cancels transfers and forgets the queue (the session ended)', () => {
    const store = useUploadsStore()
    const firstBatch = store.batchId
    store.enqueue([file('1.jpg'), file('2.jpg')])

    store.reset()

    expect(store.items).toHaveLength(0)
    expect(store.batchId).not.toBe(firstBatch)
  })

  it('rejects invalid files immediately and limits concurrent uploads to three', () => {
    const store = useUploadsStore()
    store.enqueue([
      file('1.jpg'),
      file('2.jpg'),
      file('3.jpg'),
      file('4.jpg'),
      file('bad.txt', 10, 'text/plain'),
    ])

    expect(store.items.filter((i) => i.phase === 'uploading')).toHaveLength(3)
    expect(store.items.filter((i) => i.phase === 'waiting')).toHaveLength(1)
    expect(store.items.find((i) => i.fileName === 'bad.txt')?.phase).toBe('rejected')
  })

  it('moves to processing after upload, then done when the server reports Ready', async () => {
    const store = useUploadsStore()
    store.enqueue([file('a.jpg')])
    pending[0]!.resolve({ photoId: 'p1', status: 'Queued' })
    await vi.waitFor(() => expect(store.items[0]?.phase).toBe('processing'))

    store.applyServerStatus([
      {
        photoId: 'p1',
        fileName: 'a.jpg',
        status: 'Ready',
        statusDetail: null,
        description: 'boy on a beach',
        descriptionState: 'Ready',
        renditionState: 'Ready',
        renditionVersion: 1,
        uploadedAt: '',
      },
    ])

    expect(store.items[0]?.phase).toBe('done')
    expect(store.items[0]?.description).toBe('boy on a beach')
  })

  it('reports duplicates (409) separately from failures', async () => {
    const store = useUploadsStore()
    store.enqueue([file('a.jpg'), file('b.jpg')])
    pending[0]!.reject(new ApiError(409, 'This photo is already in the library.'))
    pending[1]!.reject(new ApiError(500, 'The server had a problem. Please try again.'))

    await vi.waitFor(() => expect(store.items.map((i) => i.phase)).toEqual(['duplicate', 'failed']))
    store.clearFinished()
    expect(store.items.map((i) => i.fileName)).toEqual(['b.jpg'])
  })
})
