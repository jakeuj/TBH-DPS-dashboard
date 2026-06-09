export interface Dict {
  meta: { homeTitle: string; homeDesc: string; installTitle: string; installDesc: string;
          changelogTitle: string; changelogDesc: string };
  nav: { features: string; install: string; faq: string; download: string };
  hero: { eyebrow: string; titleA: string; titleHighlight: string; lede: string;
          ctaDownload: string; ctaGithub: string;
          trust: { mit: string; readonly: string; langs: string; tested: string } };
  stats: { damageTypes: string; panels: string; languages: string; openSource: string };
  featuresKicker: string; featuresTitle: string; featuresSub: string;
  features: { tag: string; title: string; body: string; points: string[] }[];
  install: { kicker: string; title: string; sub: string;
             steps: { title: string; body: string }[]; full: string };
  faq: { kicker: string; title: string; items: { q: string; a: string }[] };
  finalCta: { title: string; sub: string };
  footer: { license: string; disclaimer: string; disclaimerLong: string };
  changelog: { title: string; intro: string; fallback: string };
  installPage: {
    lead: string;
    firstTime: { title: string; steps: string[] };
    update: { title: string; body: string };
    blackScreen: { title: string; body: string };
    uninstall: { title: string; body: string };
    backHome: string;
  };
}
