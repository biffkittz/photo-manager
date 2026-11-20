<script setup lang="ts">
import AppHeader from '@/components/AppHeader.vue'
import BaseSpinner from '@/components/BaseSpinner.vue'
import ErrorBoundary from '@/components/ErrorBoundary.vue'
import ToastHost from '@/components/ToastHost.vue'
</script>

<template>
  <a
    href="#main-content"
    class="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded focus:bg-surface focus:p-2"
  >
    Skip to content
  </a>
  <AppHeader />
  <main id="main-content" class="mx-auto max-w-7xl px-4 py-6">
    <ErrorBoundary>
      <RouterView v-slot="{ Component }">
        <Suspense>
          <component :is="Component" />
          <template #fallback>
            <div class="flex justify-center py-20"><BaseSpinner label="Loading page" /></div>
          </template>
        </Suspense>
      </RouterView>
    </ErrorBoundary>
  </main>
  <ToastHost />
</template>
