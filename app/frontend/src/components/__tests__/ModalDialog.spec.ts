import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick, ref } from 'vue'
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

  it('lets only the innermost of two dialogs handle Escape, even when focus was lost', async () => {
    const outerOpen = ref(true)
    const innerOpen = ref(true)
    const wrapper = mount(
      defineComponent(() => () => [
        h(
          ModalDialog,
          {
            open: outerOpen.value,
            title: 'Photo',
            'onUpdate:open': (value: boolean) => (outerOpen.value = value),
          },
          { default: () => h('button', 'Add to album') },
        ),
        h(
          ModalDialog,
          {
            open: innerOpen.value,
            title: 'Add to album',
            'onUpdate:open': (value: boolean) => (innerOpen.value = value),
          },
          { default: () => h('button', 'Create album') },
        ),
      ]),
      { attachTo: document.body },
    )
    await nextTick()
    await nextTick()

    // A focused control that went away (a button now disabled) leaves focus on the body.
    ;(document.activeElement as HTMLElement | null)?.blur()
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    expect([outerOpen.value, innerOpen.value]).toEqual([true, false])

    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    expect(outerOpen.value).toBe(false)
    wrapper.unmount()
  })
})
