<script setup lang="ts">
/** Shows a new invite link once (the server keeps only its hash), with a copy button. */
import { computed, ref, useId } from 'vue'
import { urls } from '@/api/endpoints'
import type { InviteCreated } from '@/api/types'
import BaseButton from '@/components/BaseButton.vue'
import { formatDate } from '@/utils/format'

const props = defineProps<{ invite: InviteCreated }>()
const emit = defineEmits<{ done: [] }>()

const link = computed(() => urls.invite(props.invite.token))
const copied = ref<'idle' | 'copied' | 'failed'>('idle')
const linkId = useId()
const input = ref<HTMLInputElement | null>(null)

async function copy(): Promise<void> {
  try {
    await navigator.clipboard.writeText(link.value)
    copied.value = 'copied'
  } catch {
    // No clipboard access (permissions, older browser): select the text so it can be copied by hand.
    input.value?.select()
    copied.value = 'failed'
  }
}
</script>

<template>
  <div class="flex flex-col gap-3 rounded-md border border-brand/40 bg-brand/5 p-4">
    <h3 class="font-medium">Invite link for {{ invite.member.name }}</h3>
    <label :for="linkId" class="sr-only">Invite link</label>
    <div class="flex flex-wrap gap-2">
      <input
        :id="linkId"
        ref="input"
        :value="link"
        readonly
        class="min-w-0 flex-1 rounded-md border border-border bg-surface p-2 font-mono text-xs pointer-coarse:text-base"
        @focus="input?.select()"
      />
      <BaseButton variant="primary" @click="copy">Copy link</BaseButton>
    </div>
    <p class="text-sm text-muted" aria-live="polite">
      <template v-if="copied === 'copied'">Copied. </template>
      <template v-else-if="copied === 'failed'">Copy it with Ctrl+C (or ⌘C). </template>
      Send it to {{ invite.member.name }} by text or email. It works once, until
      {{ formatDate(invite.expiresAt) }}.
      <template v-if="invite.member.inviteEmail">
        Only the Google account {{ invite.member.inviteEmail }} can accept it.
      </template>
      <template v-else>Whoever opens it first can join, so send it only to them.</template>
      It is shown only now; if it gets lost, create a new link.
    </p>
    <div>
      <BaseButton variant="ghost" @click="emit('done')">Done</BaseButton>
    </div>
  </div>
</template>
