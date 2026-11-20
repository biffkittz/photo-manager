<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { urls } from '@/api/endpoints'

const route = useRoute()
const denied = computed(() => route.query.error === 'access_denied')
// The server re-validates the return URL (open-redirect protection); only local paths are accepted here too.
const returnUrl = computed(() => {
  const value = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : '/gallery'
  return value.startsWith('/') && !value.startsWith('//') ? value : '/gallery'
})
</script>

<template>
  <section
    class="mx-auto mt-16 max-w-sm rounded-lg border border-border bg-surface p-8 text-center shadow-sm"
  >
    <h1 data-page-title tabindex="-1" class="text-2xl font-bold">Photo Manager</h1>
    <p class="mt-2 text-sm text-muted">A private, encrypted family photo library.</p>
    <p v-if="denied" role="alert" class="mt-4 rounded-md bg-danger/10 p-3 text-sm text-danger">
      That GitHub account is not allowed to use Photo Manager.
    </p>
    <!-- A full-page navigation (not fetch): the OAuth flow redirects through github.com and back. -->
    <a
      :href="urls.login(returnUrl)"
      class="mt-6 inline-flex w-full items-center justify-center rounded-md bg-brand px-4 py-2 font-medium text-on-brand hover:bg-brand-hover"
    >
      Sign in with GitHub
    </a>
  </section>
</template>
