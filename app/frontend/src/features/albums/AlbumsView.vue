<script setup lang="ts">
/** All albums (shared by the family), most recently changed first, and a way to start a new one. */
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError } from '@/api/http'
import BaseButton from '@/components/BaseButton.vue'
import BaseSpinner from '@/components/BaseSpinner.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import ModalDialog from '@/components/ModalDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useAlbumsQuery, useCreateAlbum } from '@/composables/useAlbums'
import { useNotificationsStore } from '@/stores/notifications'
import AlbumCard from './AlbumCard.vue'
import AlbumNameForm from './AlbumNameForm.vue'

const router = useRouter()
const notifications = useNotificationsStore()
const albums = useAlbumsQuery()
const createAlbum = useCreateAlbum()
const creating = ref(false)

async function create(name: string): Promise<void> {
  try {
    const album = await createAlbum.mutateAsync({ name, photoIds: [] })
    creating.value = false
    await router.push({ name: 'album', params: { albumId: album.id } })
  } catch (error) {
    notifications.notify(
      'error',
      error instanceof ApiError ? error.message : 'The album could not be created.',
    )
  }
}
</script>

<template>
  <section>
    <PageHeader title="Albums">
      <template v-if="albums.data.value?.length" #actions>
        <BaseButton variant="primary" @click="creating = true">New album</BaseButton>
      </template>
    </PageHeader>

    <div v-if="albums.isPending.value" class="flex justify-center py-20">
      <BaseSpinner label="Loading albums" />
    </div>
    <ErrorAlert
      v-else-if="albums.error.value"
      :message="albums.error.value.message"
      retry-label="Retry"
      @retry="albums.refetch()"
    />
    <EmptyState v-else-if="!albums.data.value?.length" title="No albums yet">
      Put a trip, a holiday or a person's photos together. Select photos in the Gallery and choose
      Add to album, or start a new album here.
      <template #action>
        <BaseButton variant="primary" @click="creating = true">New album</BaseButton>
      </template>
    </EmptyState>
    <ul
      v-else
      aria-label="Albums"
      class="grid grid-cols-2 gap-x-3 gap-y-5 sm:grid-cols-3 lg:grid-cols-5"
    >
      <li v-for="album in albums.data.value" :key="album.id">
        <AlbumCard :album="album" />
      </li>
    </ul>

    <ModalDialog v-model:open="creating" title="New album">
      <AlbumNameForm
        submit-label="Create album"
        :saving="createAlbum.isPending.value"
        @save="create"
      />
    </ModalDialog>
  </section>
</template>
