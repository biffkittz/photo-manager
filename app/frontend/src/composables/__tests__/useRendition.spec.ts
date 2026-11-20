import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { configureHttp } from '@/api/http'
import type { ProcessingState } from '@/api/types'
import { useRendition } from '../useRendition'

function mountWith(
  state: ProcessingState | undefined,
  fetchMock: typeof fetch,
  initialUrl = '/api/photos/1/thumbnail?v=0',
) {
  vi.stubGlobal('fetch', fetchMock)
  const url = ref(initialUrl)
  const stateRef = ref(state)
  let api!: ReturnType<typeof useRendition>
  const wrapper = mount(
    defineComponent({
      setup() {
        api = useRendition(url, stateRef, {
          intervalMs: 5,
          maxAttempts: 5,
        })
        return () => null
      },
    }),
  )
  return {
    status: () => api.status.value,
    handleImageError: () => api.handleImageError(),
    url,
    state: stateRef,
    wrapper,
  }
}

describe('useRendition', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('polls while the API answers 202 and becomes ready on 200', async () => {
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(new Response(null, { status: 202 }))
      .mockResolvedValueOnce(new Response('img', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    let status!: ReturnType<typeof useRendition>['status']
    const wrapper = mount(
      defineComponent({
        setup() {
          ;({ status } = useRendition(
            ref('/api/photos/1/thumbnail?v=0'),
            ref<ProcessingState>('NotStarted'),
            {
              intervalMs: 5,
              maxAttempts: 5,
            },
          ))
          return () => null
        },
      }),
    )

    expect(status.value).toBe('pending')
    await vi.waitFor(() => expect(status.value).toBe('ready'))
    expect(fetchMock).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('does not fetch when the rendition is already ready', () => {
    const fetchMock = vi.fn<typeof fetch>()
    vi.stubGlobal('fetch', fetchMock)
    mount(
      defineComponent({
        setup: () => (useRendition(ref('/x'), ref<ProcessingState>('Ready')), () => null),
      }),
    )
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('waits for the photo to load before requesting anything', () => {
    const fetchMock = vi.fn<typeof fetch>()
    const { status } = mountWith(undefined, fetchMock)
    expect(status()).toBe('pending')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it.each(['Failed', 'Unsupported'] as const)(
    'reports %s renditions as unavailable without a request',
    (state) => {
      const fetchMock = vi.fn<typeof fetch>()
      const { status } = mountWith(state, fetchMock)
      expect(status()).toBe('unavailable')
      expect(fetchMock).not.toHaveBeenCalled()
    },
  )

  it('hands an expired session to the sign-in flow', async () => {
    const unauthorized = vi.fn<() => void>()
    configureHttp({ csrfToken: () => null, unauthorized })
    const { status } = mountWith(
      'Pending',
      vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 401 })),
    )
    await vi.waitFor(() => expect(status()).toBe('unavailable'))
    expect(unauthorized).toHaveBeenCalledOnce()
  })

  it('probes an img error, notifies unauthorized on 401, and marks unavailable', async () => {
    const unauthorized = vi.fn<() => void>()
    configureHttp({ csrfToken: () => null, unauthorized })
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 401 }))
    const { status, handleImageError, wrapper } = mountWith('Ready', fetchMock)

    expect(status()).toBe('ready')
    handleImageError()
    expect(status()).toBe('pending')
    await vi.waitFor(() => expect(status()).toBe('unavailable'))
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(unauthorized).toHaveBeenCalledOnce()
    wrapper.unmount()
  })

  it('polls on 202 regeneration after an img error and recovers to ready on 200', async () => {
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(new Response(null, { status: 202 }))
      .mockResolvedValueOnce(new Response('img', { status: 200 }))
    const { status, handleImageError, wrapper } = mountWith('Ready', fetchMock)

    handleImageError()
    expect(status()).toBe('pending')
    await vi.waitFor(() => expect(status()).toBe('ready'))
    expect(fetchMock).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('gives up on a second img error for the same URL to prevent retry loops', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response('bad', { status: 200 }))
    const { status, handleImageError, wrapper } = mountWith('Ready', fetchMock)

    handleImageError()
    await vi.waitFor(() => expect(status()).toBe('ready'))
    expect(fetchMock).toHaveBeenCalledTimes(1)

    handleImageError()
    expect(status()).toBe('unavailable')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('ignores stale in-flight poll results after url changes or unmount', async () => {
    let resolveFirst!: (response: Response) => void
    const firstFetch = new Promise<Response>((resolve) => {
      resolveFirst = resolve
    })
    const fetchMock = vi.fn<typeof fetch>().mockReturnValueOnce(firstFetch)
    const { status, url, state, wrapper } = mountWith(
      'NotStarted',
      fetchMock,
      '/api/photos/1/thumbnail?v=0',
    )

    expect(status()).toBe('pending')

    // Navigate to a different Ready photo before the first fetch settles.
    url.value = '/api/photos/2/thumbnail?v=1'
    state.value = 'Ready'
    await vi.waitFor(() => expect(status()).toBe('ready'))

    // Even if the aborted fetch resolves to 404 or 202, it must not overwrite Photo 2 or schedule timers.
    resolveFirst(new Response(null, { status: 202 }))
    await new Promise((resolve) => setTimeout(resolve, 25))
    expect(status()).toBe('ready')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })
})
