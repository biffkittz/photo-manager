import 'pinia'
import type { StateTree } from 'pinia'
import type { PersistOptions } from '@/stores/plugins/persist'

declare module 'pinia' {
  // Store option read by persistPlugin (src/stores/plugins/persist.ts).
  export interface DefineStoreOptionsBase<S extends StateTree, Store> {
    persist?: PersistOptions
  }
}
