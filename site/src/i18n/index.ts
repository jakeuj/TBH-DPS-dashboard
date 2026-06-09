import type { Locale } from '../config';
import type { Dict } from './types';
import en from './en';
import zhHant from './zh-Hant';
import zhHans from './zh-Hans';
import ja from './ja';
import es from './es';

const dicts: Record<Locale, Dict> = { en, 'zh-Hant': zhHant, 'zh-Hans': zhHans, ja, es };

export function getDict(locale: Locale): Dict {
  return dicts[locale];
}
