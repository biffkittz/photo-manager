<script setup lang="ts">
/** An album's name: for a new album, or to rename one. */
import { useId } from 'vue'
import { useForm } from 'vee-validate'
import { z } from 'zod'
import BaseButton from '@/components/BaseButton.vue'
import { toTypedSchema } from '@/utils/zodSchema'

const props = withDefaults(
  defineProps<{
    initial?: string
    label?: string
    submitLabel: string
    saving: boolean
    cancelable?: boolean
  }>(),
  { initial: '', label: 'Album name', cancelable: false },
)
const emit = defineEmits<{ save: [name: string]; cancel: [] }>()

const schema = toTypedSchema(
  z.object({
    name: z
      .string()
      .trim()
      .min(1, 'Give the album a name.')
      .max(100, 'Album names are limited to 100 characters.'),
  }),
)

const { defineField, errors, handleSubmit } = useForm({
  validationSchema: schema,
  initialValues: { name: props.initial },
})
const [name, nameAttrs] = defineField('name', {
  validateOnBlur: false,
  validateOnModelUpdate: false,
})
const inputId = useId()
const errorId = useId()

const onSubmit = handleSubmit((values) => emit('save', values.name))
</script>

<template>
  <form class="flex flex-col gap-2" novalidate @submit="onSubmit">
    <label :for="inputId" class="text-sm font-medium">{{ label }}</label>
    <div class="flex flex-wrap gap-2">
      <input
        :id="inputId"
        v-model="name"
        v-bind="nameAttrs"
        type="text"
        maxlength="100"
        autocomplete="off"
        placeholder="For example: Beach, July 2019"
        :aria-invalid="errors.name ? 'true' : 'false'"
        :aria-describedby="errors.name ? errorId : undefined"
        class="min-w-0 flex-1 basis-48 rounded-md border border-border bg-surface p-2 text-sm placeholder:text-muted pointer-coarse:min-h-11 pointer-coarse:text-base"
      />
      <BaseButton variant="primary" type="submit" :loading="saving">{{ submitLabel }}</BaseButton>
      <BaseButton v-if="cancelable" variant="ghost" @click="emit('cancel')">Cancel</BaseButton>
    </div>
    <p v-if="errors.name" :id="errorId" role="alert" class="text-sm text-danger">
      {{ errors.name }}
    </p>
  </form>
</template>
