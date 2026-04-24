import { create } from 'zustand';
import type { AppState, InboundMessage, Plugin, Profile, RepoProject } from './types';
import { onHostMessage, sendToHost } from './bridge';

interface Toast {
  id: number;
  level: 'info' | 'error';
  title: string;
  message: string;
}

interface CompileProgress {
  pluginName: string;
  message: string;
}

type BrowseKind = 'dll' | 'directory';

interface Store extends AppState {
  toasts: Toast[];
  compileProgress: CompileProgress | null;
  pendingBrowseKind: BrowseKind | null;
  isLoading: boolean;

  // Resolved by C# browse response
  _browseResolvers: Map<BrowseKind, (path: string) => void>;

  // Resolved by C# repoCsprojs response
  _repoCsprojsResolver: ((projects: RepoProject[]) => void) | null;

  dismissToast: (id: number) => void;
  requestBrowse: (kind: BrowseKind) => Promise<string | null>;
  requestRepoCsprojs: (url: string) => Promise<RepoProject[]>;
  _applyState: (s: AppState) => void;
}

let _toastSeq = 0;

export const useStore = create<Store>((set, get) => ({
  profiles: [],
  plugins: {},
  activeProfileId: null,
  isCompiling: false,
  toasts: [],
  compileProgress: null,
  pendingBrowseKind: null,
  isLoading: true,
  _browseResolvers: new Map(),
  _repoCsprojsResolver: null,

  dismissToast: (id) =>
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),

  requestBrowse: (kind) => {
    return new Promise<string | null>((resolve) => {
      get()._browseResolvers.set(kind, resolve);
      sendToHost(kind === 'dll' ? { type: 'browseDll' } : { type: 'browseDirectory' });
    });
  },

  requestRepoCsprojs: (url) => {
    return new Promise<RepoProject[]>((resolve) => {
      set({ _repoCsprojsResolver: resolve });
      sendToHost({ type: 'fetchRepoCsprojs', url });
    });
  },

  _applyState: (s) => set({ ...s, isLoading: false }),
}));

/** Wire up the host → store message pipe. Call once at app startup. */
export function initBridge(): void {
  onHostMessage((raw) => {
    let msg: InboundMessage;
    try {
      msg = JSON.parse(raw) as InboundMessage;
    } catch {
      console.error('[bridge] bad JSON', raw);
      return;
    }

    const store = useStore.getState();

    if (msg.type === 'state') {
      const { type: _t, ...state } = msg;
      store._applyState(state as AppState);
    } else if (msg.type === 'compileProgress') {
      useStore.setState({ compileProgress: { pluginName: msg.pluginName, message: msg.message } });
    } else if (msg.type === 'browseResult') {
      const resolve = store._browseResolvers.get(msg.kind);
      if (resolve) {
        store._browseResolvers.delete(msg.kind);
        resolve(msg.path);
      }
    } else if (msg.type === 'repoCsprojs') {
      const resolve = store._repoCsprojsResolver;
      if (resolve) {
        useStore.setState({ _repoCsprojsResolver: null });
        resolve(msg.projects);
      }
    } else if (msg.type === 'toast') {
      const id = ++_toastSeq;
      useStore.setState((s) => ({
        toasts: [...s.toasts, { id, level: msg.level, title: msg.title, message: msg.message }],
      }));
      setTimeout(() => useStore.getState().dismissToast(id), 6000);
    }
  });

  // Request initial state
  sendToHost({ type: 'getState' });
}

// ── Typed selectors ──────────────────────────────────────────────────────────

// Return stable object/array references — never derive inside selectors,
// as creating new arrays/objects on every call causes infinite render loops.

export const selectProfiles = (s: Store): Profile[] => s.profiles;

export const selectPluginsMap = (s: Store): Record<string, Plugin> => s.plugins;

export const selectActiveProfile = (s: Store): Profile | null =>
  s.profiles.find((p) => p.id === s.activeProfileId) ?? null;
