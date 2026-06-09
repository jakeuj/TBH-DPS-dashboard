import type { Dict } from './types';

const zhHant: Dict = {
  meta: {
    homeTitle: 'TBH DPS Meter — TaskBarHero 即時 DPS 疊圖',
    homeDesc: '免費開源的 TaskBarHero（TBH）遊戲內疊圖：即時 DPS、承受傷害、關卡逐波比較與個人化刷圖規劃。只讀資料，絕不修改遊戲數值。',
    installTitle: 'TaskBarHero 安裝教學 — TBH DPS Meter',
    installDesc: '三步驟安裝 TaskBarHero 的 TBH DPS Meter 疊圖，免編譯。',
    changelogTitle: '更新日誌 — TBH DPS Meter',
    changelogDesc: 'TBH DPS Meter（TaskBarHero 疊圖）的版本更新與發佈紀錄。',
  },
  nav: { features: '功能', install: '安裝', faq: '常見問題', download: '下載' },
  hero: {
    eyebrow: '為 TaskBarHero 打造 · BepInEx 疊圖',
    titleA: '看懂你的',
    titleHighlight: '每一次輸出',
    lede: '即時 DPS、承受傷害、關卡逐波比較、個人化刷圖規劃 —— 全部在遊戲內疊圖完成。開源、只讀資料，絕不改動任何遊戲數值。',
    ctaDownload: '免費下載最新版',
    ctaGithub: '在 GitHub 查看原始碼',
    trust: { mit: 'MIT 開源', readonly: '只讀 · 不改數值', langs: '5 種語言', tested: '已測 v1.00.09' },
  },
  stats: { damageTypes: '傷害類型', panels: '分析面板', languages: '介面語言', openSource: '開源 · 只讀' },
  featuresKicker: '功能',
  featuresTitle: '不只是個數字，是一套戰鬥儀表板',
  featuresSub: '四個面板各司其職。按一個鍵就疊在遊戲上，點擊穿透、不擋操作。',
  features: [
    {
      tag: 'F9 · 即時',
      title: 'DPS 面板',
      body: '5 秒滑動視窗的即時 DPS，加上峰值與平均。一眼看穿你的輸出結構，知道下一件裝備該補什麼。',
      points: ['即時 / 峰值 / 平均 DPS', '暴擊率與暴傷佔比', '近戰／投射／範圍／召喚／DoT／陷阱 六類拆解'],
    },
    {
      tag: 'F10 · 防禦',
      title: '承受傷害面板',
      body: '不是只看你打多痛，也看你被打多痛。找出致命的那一擊從哪來，調整你的抗性與站位。',
      points: ['DTPS、最大單擊、被擊中次數', '敵方對你的暴擊率', '物理／火／冰／雷／混沌 屬性分布'],
    },
    {
      tag: 'F11 · 比較',
      title: '關卡逐波比較',
      body: '把這次通關和你的最佳紀錄並排，連裝備與技能的差異都標出來。配速圖告訴你哪一波拖了後腿。',
      points: ['逐波時間、有效輸出 vs 待機時間', '完整角色裝備與技能對照', '清關時間趨勢圖，點任一點回看'],
    },
    {
      tag: 'F6 · 規劃',
      title: '個人化刷圖規劃',
      body: '不是抄 wiki 的死表格 —— 用你自己的真實戰績校準。gold/秒 與 exp/秒 並排排名，直接告訴你該刷哪一關。',
      points: ['已通關用實測值，未通關用個人倍率推估', 'EXP 保留率欄位反映等級懲罰', '裝備一換自動偵測，提示重新校準'],
    },
  ],
  install: {
    kicker: '安裝',
    title: '三步驟，三分鐘',
    sub: '不用編譯、不用設定。解壓、丟進資料夾、從 Steam 開。',
    steps: [
      { title: '下載 zip', body: '從 Releases 抓最新的 TBH-DpsMeter.zip，免編譯。' },
      { title: '解壓到遊戲目錄', body: '全部解壓到 TaskBarHero.exe 旁邊，需要時選覆蓋。' },
      { title: '從 Steam 啟動', body: '務必透過 Steam 開遊戲，疊圖會自動載入。' },
    ],
    full: '看完整安裝教學 →',
  },
  installPage: {
    lead: '安裝 TaskBarHero 的 TBH DPS Meter 疊圖。免編譯 —— 下載、解壓、從 Steam 啟動即可。',
    firstTime: {
      title: '首次安裝（尚未安裝 BepInEx）',
      steps: [
        '從 Releases 頁面下載 TBH-DpsMeter-vX.Y.Z.zip。',
        '在 Steam 對「TBH: Task Bar Hero」按右鍵 → 管理 → 瀏覽本機檔案（應該看到 TaskBarHero.exe）。',
        '把 zip 內所有檔案解壓到該資料夾，讓 winhttp.dll、doorstop_config.ini、dotnet 與 BepInEx 與 TaskBarHero.exe 並排（詢問時選「是」覆蓋）。',
        '透過 Steam 啟動 —— 直接開 exe 不會載入外掛。',
        '首次啟動會黑畫面 1–3 分鐘（一次性設定），之後就正常了。',
      ],
    },
    update: {
      title: '更新外掛（先前已安裝過）',
      body: '更新只需要單一個 DLL —— BepInEx 本體不用動。先完全關閉遊戲（執行中 DLL 會被鎖住），把新的 TBH.DpsMeter.dll 覆蓋到 <遊戲目錄>\\BepInEx\\plugins\\，再透過 Steam 重新啟動。面板內也有更新通知與一鍵下載。',
    },
    blackScreen: {
      title: '首次啟動黑畫面正常嗎？',
      body: '正常。首次啟動會跑一次性的 1–3 分鐘設定，之後啟動就正常了。',
    },
    uninstall: {
      title: '解除安裝',
      body: '從遊戲目錄刪除 winhttp.dll、doorstop_config.ini、.doorstop_version、dotnet\\ 資料夾與 BepInEx\\ 資料夾，即可完全還原原版遊戲。',
    },
    backHome: '← 回首頁',
  },
  faq: {
    kicker: '常見問題',
    title: '你大概想問的',
    items: [
      { q: '會被封號嗎？', a: '本工具透過 BepInEx 注入，只讀取傷害資料、不修改任何遊戲數值，且遊戲為單機。不過任何第三方 mod 或注入工具都可能違反遊戲或平台（如 Steam）的服務條款，請自行評估風險。' },
      { q: '要怎麼更新？', a: '只要把單一個 TBH.DpsMeter.dll 覆蓋到 plugins 資料夾即可 —— BepInEx 本體不用動。面板內也有更新通知與一鍵下載。' },
      { q: '第一次啟動黑畫面正常嗎？', a: '正常。首次啟動會跑一次性的 1–3 分鐘設定，之後一切正常。' },
      { q: '支援哪些遊戲版本？', a: '已於 v1.00.09（Unity 6 / IL2CPP）測試。遊戲大改版後會盡快跟進修復。' },
    ],
  },
  finalCta: { title: '準備好看清你的輸出了嗎？', sub: '免費、開源、三分鐘安裝。' },
  footer: {
    license: 'MIT 授權',
    disclaimer: '免責聲明',
    disclaimerLong: '本工具僅讀取資料、不修改遊戲數值。使用本軟體之風險由您自行承擔。© 2026 WarmBed',
  },
  changelog: {
    title: '更新日誌',
    intro: '每一個 TBH DPS Meter 版本，直接取自 GitHub。',
    fallback: '目前無法載入發佈資訊 —— 請至 GitHub 查看。',
  },
};

export default zhHant;
