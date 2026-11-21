<script setup lang="ts">
/** Invite someone: a name (how they appear in the app) and, optionally, the only Google address that may accept. */
import { useId } from 'vue'
import { useForm } from 'vee-validate'
import { z } from 'zod'
import BaseButton from '@/components/BaseButton.vue'
import { toTypedSchema } from '@/utils/zodSchema'

defineProps<{ saving: boolean; disabled: boolean }>()
const emit = defineEmits<{ invite: [name: string, email: string | null] }>()

const schema = toTypedSchema(
  z.object({
    name: z
      .string()
      .trim()
      .min(1, 'Enter a name.')
      .max(100, 'Names are limited to 100 characters.'),
    email: z
      .string()
      .trim()
      .max(320, 'That email address is too long.')
      .refine((value) => value === '' || z.email().safeParse(value).success, {
        message: 'Enter an email address like name@gmail.com, or leave it empty.',
      }),
  }),
)

const { defineField, errors, handleSubmit, resetForm } = useForm({
  validationSchema: schema,
  initialValues: { name: '', email: '' },
})
const fieldOptions = { validateOnBlur: true, validateOnModelUpdate: false }
const [name, nameAttrs] = defineField('name', fieldOptions)
const [email, emailAttrs] = defineField('email', fieldOptions)
const nameId = useId()
const nameErrorId = useId()
const emailId = useId()
const emailHelpId = useId()
const emailErrorId = useId()

const onSubmit = handleSubmit((values) => {
  emit('invite', values.name, values.email === '' ? null : values.email)
  resetForm()
})
</script>

<template>
  <form class="flex flex-col gap-4" novalidate @submit="onSubmit">
    <div class="flex flex-col gap-1">
      <label :for="nameId" class="text-sm font-medium">Name</label>
      <input
        :id="nameId"
        v-model="name"
        v-bind="nameAttrs"
        type="text"
        maxlength="100"
        autocomplete="off"
        :disabled="disabled"
        :aria-invalid="errors.name ? 'true' : 'false'"
        :aria-describedby="errors.name ? nameErrorId : undefined"
        class="rounded-md border border-border bg-surface p-2 text-sm pointer-coarse:min-h-11 pointer-coarse:text-base"
      />
      <p v-if="errors.name" :id="nameErrorId" role="alert" class="text-sm text-danger">
        {{ errors.name }}
      </p>
    </div>
    <div class="flex flex-col gap-1">
      <label :for="emailId" class="text-sm font-medium">Google account email (optional)</label>
      <input
        :id="emailId"
        v-model="email"
        v-bind="emailAttrs"
        type="email"
        maxlength="320"
        autocomplete="off"
        :disabled="disabled"
        :aria-invalid="errors.email ? 'true' : 'false'"
        :aria-describedby="errors.email ? `${emailHelpId} ${emailErrorId}` : emailHelpId"
        class="rounded-md border border-border bg-surface p-2 text-sm pointer-coarse:min-h-11 pointer-coarse:text-base"
      />
      <p :id="emailHelpId" class="text-xs text-muted">
        Recommended. Only that Google account can then accept, even if the link reaches someone
        else.
      </p>
      <p v-if="errors.email" :id="emailErrorId" role="alert" class="text-sm text-danger">
        {{ errors.email }}
      </p>
    </div>
    <div>
      <BaseButton variant="primary" type="submit" :loading="saving" :disabled="disabled">
        Create invite link
      </BaseButton>
    </div>
  </form>
</template>
