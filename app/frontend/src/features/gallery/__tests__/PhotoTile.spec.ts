import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PhotoTile from '../PhotoTile.vue'
import type { PhotoSummary } from '@/api/types'

const photo: PhotoSummary = {
  id: 'abc',
  fileName: 'IMG_1.jpg',
  description: 'woman and boy smiling on the beach',
  uploadedAt: '2024-07-04T00:00:00Z',
  takenAt: null,
  width: 100,
  height: 100,
  renditionState: 'Ready',
  renditionVersion: 2,
}

describe('PhotoTile', () => {
  it('uses the description as alt text and a versioned thumbnail URL', () => {
    const wrapper = mount(PhotoTile, { props: { photo, selected: false } })
    const img = wrapper.get('img')
    expect(img.attributes('alt')).toBe('woman and boy smiling on the beach')
    expect(img.attributes('src')).toBe('/api/photos/abc/thumbnail?v=2')
    expect(img.attributes('loading')).toBe('lazy')
  })

  it('emits open and toggle with the photo id', async () => {
    const wrapper = mount(PhotoTile, { props: { photo, selected: false } })
    await wrapper.get('button').trigger('click')
    await wrapper.get('input[type="checkbox"]').trigger('change')
    expect(wrapper.emitted('open')?.[0]).toEqual(['abc'])
    expect(wrapper.emitted('toggle')?.[0]).toEqual(['abc'])
    expect(wrapper.get('label').text()).toContain('Select woman and boy smiling on the beach')
  })
})
