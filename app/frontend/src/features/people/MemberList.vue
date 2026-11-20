<script setup lang="ts">
/** People invited from the app, with what can be done for each. */
import type { Member, MemberStatus } from '@/api/types'
import BaseButton from '@/components/BaseButton.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { formatDate } from '@/utils/format'

defineProps<{ members: Member[]; busyId: number | null }>()
const emit = defineEmits<{ renew: [member: Member]; remove: [member: Member] }>()

const tones = {
  Active: 'success',
  Invited: 'info',
  Expired: 'warning',
  Removed: 'neutral',
} as const satisfies Record<MemberStatus, string>
const labels: Record<MemberStatus, string> = {
  Active: 'Has access',
  Invited: 'Invited',
  Expired: 'Invite expired',
  Removed: 'Removed',
}

function details(member: Member): string {
  switch (member.status) {
    case 'Active':
      return [
        member.googleEmail ? `Google account ${member.googleEmail}` : 'Google account linked',
        member.lastSignInAt ? `last signed in ${formatDate(member.lastSignInAt)}` : '',
      ]
        .filter(Boolean)
        .join(', ')
    case 'Invited':
      return `Link works until ${formatDate(member.inviteExpiresAt)}${member.inviteEmail ? `, only for ${member.inviteEmail}` : ''}`
    case 'Expired':
      return `Link expired ${formatDate(member.inviteExpiresAt)}`
    case 'Removed':
      return `Removed ${formatDate(member.removedAt)}`
  }
}
</script>

<template>
  <ul class="divide-y divide-border rounded-md border border-border bg-surface">
    <li
      v-for="member in members"
      :key="member.id"
      class="flex flex-wrap items-center justify-between gap-3 p-4"
    >
      <div class="min-w-0">
        <p class="flex flex-wrap items-center gap-2 font-medium">
          {{ member.name }}
          <StatusBadge :tone="tones[member.status]" :text="labels[member.status]" />
        </p>
        <p class="mt-1 text-sm break-words text-muted">
          {{ details(member) }}. Invited by {{ member.invitedBy }}.
        </p>
      </div>
      <div class="flex gap-2">
        <BaseButton
          v-if="member.status !== 'Active'"
          :loading="busyId === member.id"
          :aria-label="`${member.status === 'Removed' ? 'Invite again' : 'New link'}: ${member.name}`"
          @click="emit('renew', member)"
        >
          {{ member.status === 'Removed' ? 'Invite again' : 'New link' }}
        </BaseButton>
        <BaseButton
          v-if="member.status !== 'Removed'"
          variant="ghost"
          :aria-label="`${member.status === 'Invited' ? 'Cancel invite' : 'Remove'}: ${member.name}`"
          @click="emit('remove', member)"
        >
          {{ member.status === 'Invited' ? 'Cancel invite' : 'Remove' }}
        </BaseButton>
      </div>
    </li>
  </ul>
</template>
