import { ref, watch, type Ref } from 'vue'

/** Returns a ref that follows `source` after it has been stable for `delayMs` (e.g. search-as-you-type). */
export function useDebouncedRef<T>(source: Ref<T>, delayMs = 300): Ref<T> {
  const debounced = ref(source.value) as Ref<T>
  watch(source, (value, _old, onCleanup) => {
    const timer = setTimeout(() => (debounced.value = value), delayMs)
    onCleanup(() => clearTimeout(timer))
  })
  return debounced
}
