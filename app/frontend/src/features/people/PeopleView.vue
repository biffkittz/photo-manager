<script setup lang="ts">
/**
 * Admin page: invite family members (they sign in with Google), send a new link, remove access. Admins and other
 * GitHub accounts are managed in Terraform and only listed here.
 */
import { computed, ref } from 'vue'
import { ApiError } from '@/api/http'
import type { InviteCreated, Member } from '@/api/types'
import BaseButton from '@/components/BaseButton.vue'
import BaseSpinner from '@/components/BaseSpinner.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import ModalDialog from '@/components/ModalDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import {
  useInviteMember,
  usePeopleQuery,
  useRemoveMember,
  useRenewInvite,
} from '@/composables/usePeople'
import { useNotificationsStore } from '@/stores/notifications'
import InviteForm from './InviteForm.vue'
import InviteLinkPanel from './InviteLinkPanel.vue'
import MemberList from './MemberList.vue'

const notifications = useNotificationsStore()
const people = usePeopleQuery()
const inviteMember = useInviteMember()
const renewInvite = useRenewInvite()
const removeMember = useRemoveMember()

const googleSignIn = computed(() => people.data.value?.googleSignIn ?? false)
const newInvite = ref<InviteCreated | null>(null)
const renewing = ref<number | null>(null)
const removing = ref<Member | null>(null)
const confirmOpen = ref(false)

function failure(error: unknown, fallback: string): void {
  notifications.notify('error', error instanceof ApiError ? error.message : fallback)
}

async function invite(name: string, email: string | null): Promise<void> {
  try {
    newInvite.value = await inviteMember.mutateAsync({ name, email })
  } catch (error) {
    failure(error, 'The invite could not be created.')
  }
}

async function renew(member: Member): Promise<void> {
  renewing.value = member.id
  try {
    newInvite.value = await renewInvite.mutateAsync(member.id)
  } catch (error) {
    failure(error, 'A new link could not be created.')
  } finally {
    renewing.value = null
  }
}

function confirmRemove(member: Member): void {
  removing.value = member
  confirmOpen.value = true
}

async function remove(): Promise<void> {
  const member = removing.value
  if (!member) return
  try {
    await removeMember.mutateAsync(member.id)
    if (newInvite.value?.member.id === member.id) {
      newInvite.value = null
    }

    notifications.notify(
      'success',
      member.status === 'Invited'
        ? `The invite for ${member.name} was cancelled.`
        : `${member.name} was removed.`,
    )
  } catch (error) {
    failure(error, 'That did not work.')
  } finally {
    confirmOpen.value = false
  }
}
</script>

<template>
  <section class="flex flex-col gap-8">
    <PageHeader title="People" />

    <BaseSpinner v-if="people.isPending.value" label="Loading people" />
    <ErrorAlert
      v-else-if="people.error.value"
      :message="people.error.value.message"
      retry-label="Retry"
      @retry="people.refetch()"
    />
    <template v-else-if="people.data.value">
      <div class="grid gap-6 md:grid-cols-2">
        <div>
          <h2 class="mb-1 text-lg font-semibold">Invite someone</h2>
          <p class="mb-3 text-sm text-muted">
            They get a link to open on their phone or computer and sign in with their Google
            account. They can view, upload and download photos; only admins see this page and
            Actions.
          </p>
          <p
            v-if="!googleSignIn"
            role="status"
            class="mb-3 rounded-md bg-warning/15 p-3 text-sm text-warning"
          >
            Google sign-in is not set up yet, so invites cannot be accepted. See "Inviting family
            members" in docs/operations.md.
          </p>
          <InviteForm
            :saving="inviteMember.isPending.value"
            :disabled="!googleSignIn"
            @invite="invite"
          />
        </div>
        <InviteLinkPanel v-if="newInvite" :invite="newInvite" @done="newInvite = null" />
      </div>

      <div>
        <h2 class="mb-3 text-lg font-semibold">Family members</h2>
        <p v-if="!people.data.value.members.length" class="text-sm text-muted">
          Nobody has been invited yet.
        </p>
        <MemberList
          v-else
          :members="people.data.value.members"
          :busy-id="renewing"
          @renew="renew"
          @remove="confirmRemove"
        />
      </div>

      <div>
        <h2 class="mb-1 text-lg font-semibold">GitHub accounts</h2>
        <p class="mb-3 text-sm text-muted">
          Managed in Terraform (<code>github_users</code>); change them with a Terraform apply.
        </p>
        <ul class="divide-y divide-border rounded-md border border-border bg-surface">
          <li
            v-for="account in people.data.value.gitHubAccounts"
            :key="account.login"
            class="flex items-center justify-between gap-3 p-4 text-sm"
          >
            <span class="font-medium">{{ account.login }}</span>
            <span class="text-muted">{{ account.role }}</span>
          </li>
        </ul>
      </div>
    </template>

    <ModalDialog
      v-model:open="confirmOpen"
      :title="removing?.status === 'Invited' ? 'Cancel invite' : 'Remove access'"
    >
      <p v-if="removing?.status === 'Invited'" class="text-sm">
        The invite link for {{ removing.name }} stops working.
      </p>
      <p v-else class="text-sm">
        {{ removing?.name }} is signed out at once and can no longer sign in. Their photos stay in
        the library. You can invite them again later.
      </p>
      <template #footer>
        <BaseButton variant="ghost" @click="confirmOpen = false">Keep</BaseButton>
        <BaseButton variant="danger" :loading="removeMember.isPending.value" @click="remove">
          {{ removing?.status === 'Invited' ? 'Cancel invite' : 'Remove' }}
        </BaseButton>
      </template>
    </ModalDialog>
  </section>
</template>
