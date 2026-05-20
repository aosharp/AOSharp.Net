export type PluginType = 'Dll' | 'Repo';

export interface RepoProject {
  name: string;
  path: string;
  isLibrary: boolean;
  section?: string | null;
  author?: string | null;
  description?: string | null;
}

export interface Plugin {
  pluginType: PluginType;
  name: string;
  path: string;
  repoUrl: string | null;
  repoBranch?: string | null;
  repoCommit?: string | null;
  projectFilePath: string | null;
  isStub: boolean;
  autoUpdate: boolean;
  isLibrary: boolean;
  section?: string | null;
  isDefault: boolean;
  isManifestDependency: boolean;
  canRemove: boolean;
  /** Set when canRemove is false (e.g. required by another plugin). */
  removeBlockedReason?: string | null;
  isCompiled: boolean;
  isEnabled: boolean;
  hasUpdate: boolean;
  trustedRepo: boolean;
  localCommit: string | null;
  remoteCommit: string | null;
  author?: string | null;
  description?: string | null;
  dependencyRepoUrls?: string[] | null;
}

export interface Loadout {
  id: string;
  name: string;
  pluginKeys: string[];
  isLocked: boolean;
  isDefault: boolean;
}

export interface Profile {
  id: string;
  name: string;
  loadoutId: string;
  isInjected: boolean;
  isActive: boolean;
}

export type InjectQueueStatus = 'pending' | 'injecting' | 'succeeded' | 'failed';

export interface InjectQueueItem {
  profileId: string;
  profileName: string;
  status: InjectQueueStatus;
  message?: string | null;
}

export type AppUpdateStatus =
  | 'Idle'
  | 'Checking'
  | 'UpToDate'
  | 'Available'
  | 'Downloading'
  | 'Ready'
  | 'Error';

export interface AppUpdateState {
  currentVersion: string;
  availableVersion: string | null;
  releaseNotesUrl: string | null;
  status: AppUpdateStatus;
  downloadProgressPercent: number;
  error: string | null;
  readyToApply: boolean;
  bannerVisible: boolean;
}

export interface AppState {
  profiles: Profile[];
  loadouts: Loadout[];
  plugins: Record<string, Plugin>;
  activeProfileId: string | null;
  autoInject: boolean;
  isCompiling: boolean;
  isInjecting: boolean;
  injectQueue: InjectQueueItem[];
  appUpdate: AppUpdateState;
}

// ── Messages C# → React ─────────────────────────────────────────────────────

export type InboundMessage =
  | { type: 'state' } & AppState
  | { type: 'injectProgress'; isInjecting: boolean; queue: InjectQueueItem[] }
  | { type: 'compileProgress'; pluginName: string; message: string }
  | { type: 'browseResult'; kind: 'dll' | 'directory'; path: string }
  | { type: 'repoCsprojs'; projects: RepoProject[] }
  | { type: 'toast'; level: 'info' | 'error'; title: string; message: string; openLogOnClick?: boolean }
  | {
      type: 'appUpdateState';
      currentVersion: string;
      availableVersion: string | null;
      releaseNotesUrl: string | null;
      status: AppUpdateStatus;
      downloadProgressPercent: number;
      error: string | null;
      readyToApply: boolean;
      bannerVisible: boolean;
    };

// ── Messages React → C# ─────────────────────────────────────────────────────

export type OutboundMessage =
  | { type: 'getState' }
  | { type: 'selectProfile'; profileId: string }
  | { type: 'inject' }
  | { type: 'eject' }
  | { type: 'compileAll' }
  | { type: 'compilePlugin'; key: string }
  | { type: 'updatePlugin'; key: string; trustRepo?: boolean }
  | { type: 'checkUpdates' }
  | { type: 'checkAppUpdate' }
  | { type: 'downloadAppUpdate' }
  | { type: 'applyAppUpdate' }
  | { type: 'dismissAppUpdate' }
  | { type: 'addDllPlugin'; path: string }
  | { type: 'addRepoPlugin'; url: string; branch?: string; commit?: string; projectFilePath: string }
  | { type: 'removePlugin'; key: string }
  | { type: 'openUrl'; url: string }
  | { type: 'openLogFile' }
  | { type: 'browseDll' }
  | { type: 'browseDirectory' }
  | { type: 'fetchRepoCsprojs'; url: string; branch?: string; commit?: string }
  | { type: 'enableLargeAddressAware'; installDir: string }
  | { type: 'assignLoadout'; profileId: string; loadoutId: string }
  | { type: 'createLoadout'; name: string; pluginKeys?: string[]; sourceLoadoutId?: string }
  | { type: 'updateLoadout'; loadoutId: string; name?: string; pluginKeys?: string[] }
  | { type: 'deleteLoadout'; loadoutId: string }
  | { type: 'duplicateLoadout'; loadoutId: string; name: string }
  | { type: 'setAutoInject'; enabled: boolean };
