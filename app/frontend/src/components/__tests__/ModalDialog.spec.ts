import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import ModalDialog from '../ModalDialog.vue'

describe('ModalDialog', () => {
  it('is an accessible modal that focuses its content and closes on Escape', async () => {
    const wrapper = mount(ModalDialog, {
      props: {
        open: true,
        title: 'Start job',
        'onUpdate:open': (value: boolean) => wrapper.setProps({ open: value }),
      },
      slots: { default: '<button id="inside">OK</button>' },
      attachTo: document.body,
    })
    await nextTick()
    await nextTick()

    const dialog = document.querySelector('[role="dialog"]')!
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)?.textContent).toBe(
      'Start job',
    )
    expect(dialog.contains(document.activeElement)).toBe(true)

    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    expect(wrapper.emitted('update:open')?.[0]).toEqual([false])
    wrapper.unmount()
  })
})
