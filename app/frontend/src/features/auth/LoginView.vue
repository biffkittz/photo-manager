<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute, useRouter } from 'vue-router'
import { urls } from '@/api/endpoints'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const { googleSignIn } = storeToRefs(useAuthStore())

/**
 * Why the last sign-in did not work (`?error=`), as set by the server. Unknown values show nothing.
 * `signin_failed`: cancelled, expired, or a configuration problem the operator sees in the logs.
 * `unavailable`: the session could not be checked (offline or a server error), which does not mean signed out.
 */
const messages: Record<string, string> = {
  access_denied: 'That GitHub account is not allowed to use Photo Manager.',
  google_denied:
    'That Google account does not have access. If you were invited, open your invite link again, or ask for a new one.',
  invite_invalid:
    'That invite link no longer works: it expired, was used already, or was replaced by a newer one. Ask for a new link.',
  invite_wrong_account:
    'That invite is for a different Google account. Open the invite link again and choose the account it was sent to.',
  invite_account_in_use:
    'That Google account already has access. Use "Continue with Google" below to sign in.',
  signin_failed: 'Signing in did not complete. Please try again.',
}
const error = computed(() => (typeof route.query.error === 'string' ? route.query.error : ''))
const message = computed(() => messages[error.value] ?? '')
const unavailable = computed(() => error.value === 'unavailable')

// The server re-validates the return URL (open-redirect protection); only local paths are accepted here too.
const returnUrl = computed(() => {
  const value = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : '/gallery'
  return value.startsWith('/') && !value.startsWith('//') ? value : '/gallery'
})

function tryAgain(): void {
  // Navigating re-runs the session check (it was not recorded as done).
  void router.replace(returnUrl.value)
}
</script>

<template>
  <section
    class="mx-auto mt-16 max-w-sm rounded-lg border border-border bg-surface p-8 text-center shadow-sm"
  >
    <h1 data-page-title tabindex="-1" class="text-2xl font-bold">Photo Manager</h1>
    <p class="mt-2 text-sm text-muted">A private, encrypted family photo library.</p>
    <p v-if="message" role="alert" class="mt-4 rounded-md bg-danger/10 p-3 text-sm text-danger">
      {{ message }}
    </p>
    <div
      v-if="unavailable"
      role="alert"
      class="mt-4 rounded-md bg-danger/10 p-3 text-sm text-danger"
    >
      <p>Photo Manager could not be reached. Check your connection and try again.</p>
      <button
        type="button"
        class="mt-2 rounded-md border border-border bg-surface px-3 py-1 font-medium text-text hover:bg-surface-muted"
        @click="tryAgain"
      >
        Try again
      </button>
    </div>
    <!-- Full-page navigations (not fetch): the OAuth flows redirect through Google or GitHub and back. -->
    <template v-if="googleSignIn">
      <a
        :href="urls.googleLogin(returnUrl)"
        class="mt-6 inline-flex w-full items-center justify-center rounded-md bg-brand px-4 py-2 font-medium text-on-brand hover:bg-brand-hover"
      >
        Continue with Google
      </a>
      <p class="mt-2 text-xs text-muted">
        Family members: use the Google account you accepted your invite with.
      </p>
      <a
        :href="urls.login(returnUrl)"
        class="mt-4 inline-flex w-full items-center justify-center rounded-md border border-border bg-surface px-4 py-2 font-medium text-text hover:bg-surface-muted"
      >
        Sign in with GitHub
      </a>
    </template>
    <a
      v-else
      :href="urls.login(returnUrl)"
      class="mt-6 inline-flex w-full items-center justify-center rounded-md bg-brand px-4 py-2 font-medium text-on-brand hover:bg-brand-hover"
    >
      Sign in with GitHub
    </a>
  </section>
</template>
