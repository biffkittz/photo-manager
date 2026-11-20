<script setup lang="ts">
/**
 * Local error boundary (errorCaptured): a crashing view shows a friendly fallback with a retry instead of
 * blanking the whole app. The error still reaches the global handler for reporting.
 */
import { onErrorCaptured, ref } from 'vue'
import ErrorAlert from './ErrorAlert.vue'

const failed = ref(false)
const attempt = ref(0)

onErrorCaptured(() => {
  failed.value = true
  return true // keep propagating to app.config.errorHandler
})

function retry(): void {
  failed.value = false
  attempt.value++
}
</script>

<template>
  <ErrorAlert
    v-if="failed"
    message="Something went wrong while showing this page."
    retry-label="Try again"
    @retry="retry"
  />
  <div v-else :key="attempt"><slot /></div>
</template>
