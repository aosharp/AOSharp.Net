export type PluginType = 'Dll' | 'Repo';

export interface RepoProject {
  name: string;
  path: string;
  isLibrary: boolean;
}

export interface Plugin {
  pluginType: PluginType;
  name: string;
  version: string | null;
  path: string;
  repoUrl: string | null;
  projectFilePath: string | null;
  isStub: boolean;
  autoUpdate: boolean;
  isLibrary: boolean;
  isDefault: boolean;
  isCompiled: boolean;
  isEnabled: boolean;
  hasUpdate: boolean;
  trustedRepo: boolean;
  localCommit: string | null;
  remoteCommit: string | null;
}

export interface Profile {
  id: string;
  name: string;
  isInjected: boolean;
  isActive: boolean;
  enabledPlugins: string[];
}

export interface AppState {
  profiles: Profile[];
  plugins: Record<string, Plugin>;
  activeProfileId: string | null;
  isCompiling: boolean;
}

// ── Messages C# → React ─────────────────────────────────────────────────────

export type InboundMessage =
  | { type: 'state' } & AppState
  | { type: 'compileProgress'; pluginName: string; message: string }
  | { type: 'browseResult'; kind: 'dll' | 'directory'; path: string }
  | { type: 'repoCsprojs'; projects: RepoProject[] }
  | { type: 'toast'; level: 'info' | 'error'; title: string; message: string };

// ── Messages React → C# ─────────────────────────────────────────────────────

export type OutboundMessage =
  | { type: 'getState' }
  | { type: 'selectProfile'; profileId: string }
  | { type: 'inject' }
  | { type: 'eject' }
  | { type: 'compileAll' }
  | { type: 'compilePlugin'; key: string }
  | { type: 'updatePlugin'; key: string }
  | { type: 'checkUpdates' }
  | { type: 'setTrustedRepo'; key: string; trusted: boolean }
  | { type: 'addDllPlugin'; path: string }
  | { type: 'addRepoPlugin'; url: string; projectFilePath: string }
  | { type: 'removePlugin'; key: string }
  | { type: 'togglePlugin'; key: string; enabled: boolean }
  | { type: 'browseDll' }
  | { type: 'browseDirectory' }
  | { type: 'fetchRepoCsprojs'; url: string }
  | { type: 'enableLargeAddressAware'; installDir: string };
