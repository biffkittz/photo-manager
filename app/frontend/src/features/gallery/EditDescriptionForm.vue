<script setup lang="ts">
/**
 * Description editor: VeeValidate + Zod with a controlled v-model, validation on blur and submit, and an
 * accessible error message (aria-invalid + aria-describedby + role=alert).
 */
import { useId } from 'vue'
import { useForm } from 'vee-validate'
import { z } from 'zod'
import BaseButton from '@/components/BaseButton.vue'
import { toTypedSchema } from '@/utils/zodSchema'

const props = defineProps<{ initial: string | null; saving: boolean }>()
const emit = defineEmits<{ save: [description: string]; cancel: [] }>()

const schema = toTypedSchema(
  z.object({
    description: z.string().trim().max(300, 'Descriptions are limited to 300 characters.'),
  }),
)

const { defineField, errors, handleSubmit } = useForm({
  validationSchema: schema,
  initialValues: { description: props.initial ?? '' },
})
const [description, descriptionAttrs] = defineField('description', {
  validateOnBlur: true,
  validateOnModelUpdate: false,
})
const fieldId = useId()
const errorId = useId()

const onSubmit = handleSubmit((values) => emit('save', values.description))
</script>

<template>
  <form class="flex flex-col gap-2" novalidate @submit="onSubmit">
    <label :for="fieldId" class="text-sm font-medium">Description</label>
    <textarea
      :id="fieldId"
      v-model="description"
      v-bind="descriptionAttrs"
      v-autofocus
      rows="4"
      maxlength="320"
      :aria-invalid="errors.description ? 'true' : 'false'"
      :aria-describedby="errors.description ? errorId : undefined"
      class="rounded-md border border-border bg-surface p-2 text-sm pointer-coarse:text-base"
    />
    <p v-if="errors.description" :id="errorId" role="alert" class="text-sm text-danger">
      {{ errors.description }}
    </p>
    <div class="flex justify-end gap-2">
      <BaseButton variant="ghost" @click="emit('cancel')">Cancel</BaseButton>
      <BaseButton variant="primary" type="submit" :loading="saving">Save</BaseButton>
    </div>
  </form>
</template>
