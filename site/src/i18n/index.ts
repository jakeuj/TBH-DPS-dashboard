import type { Locale } from '../config';
import type { Dict } from './types';
import en from './en';

const dicts: Partial<Record<Locale, Dict>> = { en };

export function getDict(locale: Locale): Dict {
  return dicts[locale] ?? en;
}
