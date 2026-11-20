import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { configureHttp } from '@/api/http'
import type { ProcessingState } from '@/api/types'
import { useRendition } from '../useRendition'

function mountWith(state: ProcessingState | undefined, fetchMock: typeof fetch) {
  vi.stubGlobal('fetch', fetchMock)
  let status!: ReturnType<typeof useRendition>['status']
  const wrapper = mount(
    defineComponent({
      setup() {
        ;({ status } = useRendition(ref('/api/photos/1/thumbnail?v=0'), ref(state), {
          intervalMs: 5,
          maxAttempts: 5,
        }))
        return () => null
      },
    }),
  )
  return { status: () => status.value, wrapper }
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
})
