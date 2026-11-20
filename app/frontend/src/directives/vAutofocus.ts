import type { Directive } from 'vue'

/** `v-autofocus`: focuses the element when it mounts (used for the first field of dialogs/forms). */
export const vAutofocus: Directive<HTMLElement> = {
  mounted: (element) => element.focus(),
}
