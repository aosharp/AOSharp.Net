import type { Plugin } from './types';

export const PLUGIN_SECTIONS = ['Library', 'Utility', 'Combat', 'Bots', 'Other'] as const;
export type PluginSection = (typeof PLUGIN_SECTIONS)[number];

export function getPluginSection(plugin: Plugin): PluginSection {
  if (plugin.isLibrary) return 'Library';
  const s = plugin.section?.trim();
  if (s && (PLUGIN_SECTIONS as readonly string[]).includes(s)) return s as PluginSection;
  return 'Other';
}
