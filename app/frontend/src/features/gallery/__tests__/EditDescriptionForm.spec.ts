import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { vAutofocus } from '@/directives/vAutofocus'
import EditDescriptionForm from '../EditDescriptionForm.vue'

function mountForm(initial: string | null = 'old') {
  return mount(EditDescriptionForm, {
    props: { initial, saving: false },
    global: { directives: { autofocus: vAutofocus } },
  })
}

describe('EditDescriptionForm', () => {
  it('rejects descriptions over 300 characters with an accessible error', async () => {
    const wrapper = mountForm()
    await wrapper.get('textarea').setValue('a'.repeat(301))
    await wrapper.get('form').trigger('submit')
    // VeeValidate validates asynchronously across several ticks.
    await vi.waitFor(() => expect(wrapper.find('[role="alert"]').exists()).toBe(true), {
      timeout: 2000,
    })

    const error = wrapper.get('[role="alert"]')
    expect(error.text()).toBe('Descriptions are limited to 300 characters.')
    expect(wrapper.get('textarea').attributes('aria-invalid')).toBe('true')
    expect(wrapper.get('textarea').attributes('aria-describedby')).toBe(error.attributes('id'))
    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('emits the trimmed value on valid submit', async () => {
    const wrapper = mountForm(null)
    await wrapper.get('textarea').setValue('  family picnic  ')
    await wrapper.get('form').trigger('submit')
    await vi.waitFor(() => expect(wrapper.emitted('save')?.[0]).toEqual(['family picnic']), {
      timeout: 2000,
    })
  })
})
