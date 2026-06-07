# TBH DPS Meter

[English](README.md) · **日本語** · [繁體中文](README.zh-Hant.md) · [简体中文](README.zh-Hans.md)

**TaskBarHero**（TBH: Task Bar Hero）向けのゲーム内 **DPS / 被ダメージ** オーバーレイ。
BepInEx 6 IL2CPP プラグインとして実装。動作確認バージョン **v1.00.09**（Unity 6 / IL2CPP）。
UI は **English / 日本語 / 繁體中文 / 简体中文 / Español** を自動判別します。

> ⬇️ **プレイヤーの方は [Releases](../../releases/latest) の zip をダウンロードするだけでOK。ビルド不要です。**

![ゲーム内オーバーレイ](image/POWERPNT_xbsYSkt6By.png)

<table>
<tr>
<td><img src="image/TaskBarHero_FkEGMBj3Kq.png" alt="DPSパネル"></td>
<td><img src="image/TaskBarHero_3TGLxaOOR2.png" alt="被ダメージパネル"></td>
</tr>
<tr>
<td align="center"><b>DPSパネル</b>（与ダメージ）</td>
<td align="center"><b>被ダメージパネル</b>（受けたダメージ）</td>
</tr>
</table>

---

## 表示内容

**DPSパネル:**
- リアルタイムDPS（5秒スライディングウィンドウ）+ ピーク + 平均
- 総ダメージ + 戦闘時間 + ウェーブ数
- ダメージタイプ別内訳（近接 / 投射 / 範囲 / 召喚 / 継続 / 罠、複合フラグ対応）
- 会心率 + 会心ダメージ割合

**被ダメージパネル:**
- リアルタイムDTPS（毎秒被ダメージ）+ ピーク + 平均
- 総被ダメージ + 時間 + 最大単発被弾
- **被弾**（攻撃を受けた回数）+ **被会心**（敵があなたに与えた会心率）
- 2本の分布バー：属性（物理/火/氷/雷/混沌）とダメージタイプ

## ステージ比較（F11）
**F11** で**ステージ比較パネル**を開きます：保存した記録をステージ別にまとめ、今回の記録を**基準**
（既定は最速クリア、または手動で固定した記録）と比較し、時間・**有効出力 vs 停止（移動）時間**・
平均/ピーク/会心・ダメージ配分・**波別時間**、そして**装備とスキルの変更**を表示します
（装備変更でクリア効率がどれだけ変わったか分かります）。◀ ▶ で記録切替、≪ ≫ でステージ切替、固定ボタンで基準設定。

## 操作
- **F9** — DPSパネルの表示/非表示（設定可：`ToggleKey`）
- **F10** — 被ダメージパネルの表示/非表示（設定可：`TakenUI.ToggleKey`）
- **F11** — ステージ比較パネルの表示/非表示（設定可：`CompareUI.ToggleKey`）
- **マウスドラッグ** — パネルの移動（位置は個別に自動保存）
- 右上の **リセット** ボタンでゼロに、**◀ ▶** で過去ステージの記録を閲覧
- **PageUp / PageDown** — パネルの透明度調整（両パネル共通）

> ⚠️ クリックはゲーム側に**貫通**します（プラグインはマウスを読み取るだけで入力を奪いません）。パネルをクリックしてもキャラが動くのは正常な挙動です。

---

## インストール

### A. 初回インストール（BepInEx 未導入）
1. **[Releases](../../releases/latest)** から `TBH-DpsMeter-vX.Y.Z.zip` をダウンロード。
2. Steam → 「TBH: Task Bar Hero」を右クリック → 管理 → ローカルファイルを閲覧
   （`TaskBarHero.exe` が見えるフォルダ）。
3. zip 内の**すべてのファイル**をそのフォルダへ解凍し、`winhttp.dll`・`doorstop_config.ini`・
   `dotnet`・`BepInEx` を `TaskBarHero.exe` と**同じ階層**に置く（上書きを聞かれたら「はい」）。
4. **必ず Steam から起動**してください（exe を直接実行するとプラグインが読み込まれません）。
5. 初回起動は 1～3 分ほど黒画面になります（一度きりの初期化）。以降は通常通りです。

### B. プラグインの更新（導入済みの場合）
**はい、更新は DLL 1ファイルの差し替えだけでOKです。** BepInEx 本体はそのままで構いません。

新しい `TBH.DpsMeter.dll` を以下に上書き：
```
<ゲームフォルダ>\BepInEx\plugins\TBH.DpsMeter.dll
```
> 上書き前に**ゲームを完全に終了**してください（起動中は DLL がロックされ上書きできません）。その後 Steam から再起動します。

---

## 設定
ファイル：`<ゲームフォルダ>\BepInEx\config\tbh.dpsmeter.cfg`（初回起動後に生成）
```
[General]
Language = Auto   # zh-Hant / zh-Hans / en / ja / es に変更で言語を固定
```

## アンインストール
ゲームフォルダから次を削除：`winhttp.dll`・`doorstop_config.ini`・`.doorstop_version`・
`dotnet\`・`BepInEx\`。これで完全にバニラ状態へ戻ります。

---

## ソースからのビルド（開発者向け）
```
dotnet build DpsMeter/DpsMeter.csproj -c Release
# 出力：DpsMeter\bin\Release\TBH.DpsMeter.dll
copy DpsMeter\bin\Release\TBH.DpsMeter.dll  <ゲーム>\BepInEx\plugins\
```
ゲームは **Steam 経由**で再起動してください（この Unity 6 ビルドでは exe 直接起動だと
BepInEx の winhttp プロキシが注入されません）。

### 仕組み
- **与ダメージ:** `TaskbarHero.Monster.ebj(DamageInfo, bool)` への Harmony postfix。
  `Unit.b_isHero` でプレイヤー側ヒットに絞り、`OriginDamage` / `IsCritical` / `DamageType` を読み取り。
- **被ダメージ:** `TaskbarHero.Hero.ebj(DamageInfo, bool)` への Harmony postfix。攻撃者がヒーロー以外の
  ヒットを計上し、`OriginDamage` / `IsCritical` / `DamageType` / `DamageAttribute` を読み取り。
- **ウェーブ境界:** `StageManager.stageState`（MONSTERSPAWN → BATTLE → REORGANIZATION）をポーリング。
  MONSTERSPAWN ごとにリセット、REORGANIZATION で凍結。
- DPS / DTPS の計算は純粋な C# の `DpsTracker` / `DamageTakenTracker`（`TrackerTests` でユニットテスト済み）。

---

## ⚠️ 免責事項
本プラグインは BepInEx で注入し、ダメージデータを**読み取るのみ**で、ゲームの数値を一切改変せず、
本作はシングルプレイです。とはいえ、**いかなるサードパーティMod／インジェクションツールも、ゲームや
プラットフォーム（Steam等）の利用規約に違反する可能性があり**、アカウント停止・セーブ破損・その他の
損失のリスクを伴います。

**本ソフトの使用はすべて自己責任です。** 本プラグインの使用に起因するアカウントの BAN・停止・データ消失・
その他の直接的または間接的損害について、作者は**一切責任を負いません**。この条件に同意できない場合は使用しないでください。

## ライセンス
[MIT](LICENSE) © 2026 WarmBed
