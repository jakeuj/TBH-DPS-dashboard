import type { Dict } from './types';

const ja: Dict = {
  meta: {
    homeTitle: 'TBH DPS Meter — TaskBarHero リアルタイム DPS オーバーレイ',
    homeDesc: 'TaskBarHero（TBH）向け無料オープンソースのゲーム内オーバーレイ。リアルタイム DPS、被ダメージ、ウェーブ別ステージ比較、個人ファーミングプランナー搭載。読み取り専用 — ゲーム値は一切変更しません。',
    installTitle: 'インストール — TBH DPS Meter for TaskBarHero',
    installDesc: 'TaskBarHero の TBH DPS Meter オーバーレイを3ステップでインストール。コンパイル不要。',
    changelogTitle: 'チェンジログ — TBH DPS Meter',
    changelogDesc: 'TBH DPS Meter（TaskBarHero オーバーレイ）のリリースノートとバージョン履歴。',
  },
  nav: { features: '機能', install: 'インストール', faq: 'よくある質問', download: 'ダウンロード' },
  hero: {
    eyebrow: 'TaskBarHero 向け · BepInEx オーバーレイ',
    titleA: 'すべての',
    titleHighlight: 'ダメージを把握する',
    lede: 'リアルタイム DPS、被ダメージ、ウェーブ別ステージ比較、個人ファーミングプランナー — すべてゲーム内オーバーレイで表示。オープンソース、読み取り専用、ゲーム値は一切変更しません。',
    ctaDownload: '最新版を無料ダウンロード',
    ctaGithub: 'GitHub でソースを見る',
    trust: { mit: 'MIT オープンソース', readonly: '読み取り専用 · 値変更なし', langs: '5言語対応', tested: 'v1.00.09 テスト済み' },
  },
  stats: { damageTypes: 'ダメージタイプ', panels: '分析パネル', languages: '対応言語', openSource: 'オープンソース · 読み取り専用' },
  featuresKicker: '機能',
  featuresTitle: '数字だけじゃない — 完全な戦闘ダッシュボード',
  featuresSub: '4つのパネル、それぞれが1つの役割を持つ。キー1つでゲームに重ねて表示、クリック透過でプレイを妨げない。',
  features: [
    {
      tag: 'F9 · ライブ',
      title: 'DPS パネル',
      body: '5秒スライディングウィンドウのリアルタイム DPS、ピークと平均も表示。ダメージ構成を一目で把握し、次に強化すべきステータスがわかる。',
      points: ['ライブ / ピーク / 平均 DPS', 'クリット率とクリットダメージ割合', '近接／投射／範囲／召喚／DoT／罠 の6種別内訳'],
    },
    {
      tag: 'F10 · 防御',
      title: '被ダメージパネル',
      body: '与えるダメージだけでなく、受けるダメージも重要。致命の一撃がどこから来るかを特定し、耐性とポジションを調整しよう。',
      points: ['DTPS、最大単発ヒット、被弾回数', 'モンスターのあなたへのクリット率', '物理／炎／氷／雷／カオス の属性分布'],
    },
    {
      tag: 'F11 · 比較',
      title: 'ウェーブ別ステージ比較',
      body: '今回のクリアとベストクリアを並べて比較 — 装備とスキルの差まで表示。ペースチャートでどのウェーブで遅れたかが一目瞭然。',
      points: ['ウェーブ別時間、アクティブ vs アイドル時間', 'キャラクター装備＆スキル構成の差分', 'クリアタイムトレンドチャート — 任意の点をクリックして詳細確認'],
    },
    {
      tag: 'F6 · プラン',
      title: '個人ファーミングプランナー',
      body: '静的な wiki テーブルではない — あなた自身の実際のプレイで校正される。gold/秒 と exp/秒 を並べてランキング、どのステージを周回すべきか直接教えてくれる。',
      points: ['クリア済みステージは実測値、未クリアは個人倍率で推定', 'EXP保持率列でレベルペナルティを反映', '装備変更を自動検知し、再クリアを促す'],
    },
  ],
  install: {
    kicker: 'インストール',
    title: '3ステップ、3分',
    sub: 'コンパイル不要、設定不要。解凍してフォルダに入れて、Steam から起動するだけ。',
    steps: [
      { title: 'zip をダウンロード', body: 'Releases から最新の TBH-DpsMeter.zip を入手 — コンパイル不要。' },
      { title: 'ゲームフォルダに解凍', body: 'TaskBarHero.exe の隣にすべて解凍し、上書きを求められたら許可。' },
      { title: 'Steam から起動', body: '必ず Steam 経由でゲームを起動 — オーバーレイが自動的に読み込まれます。' },
    ],
    full: '完全インストールガイドを見る →',
  },
  installPage: {
    lead: 'TaskBarHero の TBH DPS Meter オーバーレイをインストール。コンパイル不要 — ダウンロード、解凍、Steam から起動するだけ。',
    firstTime: {
      title: '初回インストール（BepInEx 未インストールの場合）',
      steps: [
        'Releases ページから TBH-DpsMeter-vX.Y.Z.zip をダウンロード。',
        'Steam で「TBH: Task Bar Hero」を右クリック → 管理 → ローカルファイルを閲覧（TaskBarHero.exe が見えるはず）。',
        'zip 内のすべてのファイルをそのフォルダに解凍し、winhttp.dll、doorstop_config.ini、dotnet と BepInEx が TaskBarHero.exe の隣に並ぶようにする（上書きを求められたら「はい」を選択）。',
        'Steam 経由で起動 — exe を直接起動してもプラグインは読み込まれません。',
        '初回起動は黒い画面が 1〜3 分続きます（一回限りのセットアップ）。その後は正常に起動します。',
      ],
    },
    update: {
      title: 'プラグインの更新（既にインストール済みの場合）',
      body: '更新に必要なのは単一の DLL のみ — BepInEx 本体はそのまま。まずゲームを完全に終了し（起動中は DLL がロックされます）、新しい TBH.DpsMeter.dll を <ゲームフォルダ>\\BepInEx\\plugins\\ に上書きして、Steam から再起動。パネル内にもアプリ内更新通知とワンクリックダウンロードがあります。',
    },
    blackScreen: {
      title: '初回起動の黒い画面は正常ですか？',
      body: 'はい、正常です。初回起動は一回限りの 1〜3 分のセットアップを行います。その後の起動は正常です。',
    },
    uninstall: {
      title: 'アンインストール',
      body: 'ゲームフォルダから winhttp.dll、doorstop_config.ini、.doorstop_version、dotnet\\ フォルダ、BepInEx\\ フォルダを削除すれば、ゲームが完全に元の状態に戻ります。',
    },
    backHome: '← ホームに戻る',
  },
  faq: {
    kicker: 'よくある質問',
    title: 'きっと気になること',
    items: [
      { q: 'BANされますか？', a: 'このツールは BepInEx 経由で注入し、ダメージデータを読み取るのみでゲーム値を一切変更しません。また、ゲームはシングルプレイヤーです。ただし、いかなるサードパーティ mod や注入ツールも、ゲームやプラットフォーム（Steam など）の利用規約に違反する可能性があります — 自己責任でご利用ください。' },
      { q: '更新方法は？', a: 'plugins フォルダの TBH.DpsMeter.dll を単純に上書きするだけ — BepInEx 本体は不要。パネル内にもアプリ内更新通知とワンクリックダウンロードがあります。' },
      { q: '初回起動の黒い画面は正常ですか？', a: 'はい。初回起動は一回限りの 1〜3 分のセットアップを行い、その後はすべて正常です。' },
      { q: '対応ゲームバージョンは？', a: 'v1.00.09（Unity 6 / IL2CPP）でテスト済み。ゲームの大型アップデート後はできる限り速やかに修正を提供します。' },
    ],
  },
  finalCta: { title: '自分のダメージをはっきり見る準備はできていますか？', sub: '無料、オープンソース、3分でインストール。' },
  footer: {
    license: 'MIT ライセンス',
    disclaimer: '免責事項',
    disclaimerLong: 'このツールはデータを読み取るのみで、ゲーム値を変更しません。本ソフトウェアの使用はすべて自己責任で行ってください。© 2026 WarmBed',
  },
  changelog: {
    title: 'チェンジログ',
    intro: 'TBH DPS Meter のすべてのリリース、GitHub から直接取得。',
    fallback: 'リリース情報を読み込めませんでした — GitHub でご確認ください。',
  },
};

export default ja;
