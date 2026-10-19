// (PROTOCOL 25) The neighbourhood-polish tool's panel body: its own button next to Placemaker's opens it (Panel.tsx
// PolishPanel draws the window, the header and the footer with Polish and Unpolish). Top to bottom: 1 pick buildings (the
// area tools), what the area holds, 2 a style (the themes as Placemaker's Style chips); Look, the options Polish gives (rows
// like any type's options; saved on each building it polishes, new buildings get them too) and Whole city (on or off, new
// buildings and each kind's theme, polishing or unpolishing every building, asking first), both folded.
import React, { ReactNode, useState } from "react";
import classNames from "classnames";
import { PainterState, PolishCity, PolishLook, ToolState } from "../contract";
import { TFunc, useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { Btn, Collapsible, Icon, Segmented, Switch } from "./common";
import { Slider } from "../controls/Slider";
import { Confirm } from "./dialog";
import { act } from "../lib/bindings";
import { categoryColor } from "../lib/palette";
import { Chip, RowLabel } from "../stages/PresetBrowser";
import b from "../stages/browser.module.scss";
import d from "../stages/design.module.scss";
import r from "../stages/review.module.scss";
import c from "../controls/controls.module.scss";
import s from "./painter.module.scss";

/** The themes, as the Style row shows them (PresetBrowser) and the card names them. */
export const POLISH_THEMES: { id: string; icon: string; en: string; tip: string }[] = [
    { id: "zone", icon: "zone", en: "By kind", tip: "Each building gets the theme its kind has under Whole city" },
    { id: "garden", icon: "flower", en: "Garden", tip: "Lawns, beds and bushes, now and then a sand pit or a bench" },
    { id: "natural", icon: "rocks", en: "Natural", tip: "Painted ground and meadow, rock outcrops, log piles" },
    { id: "tidy", icon: "topiary", en: "Tidy", tip: "Lawns, hedges and flower beds, no rocks" },
    { id: "yard", icon: "crate", en: "Yard", tip: "Worn ground, rubble piles, pallets and crates" },
];

export const themeName = (t: TFunc, id: string) => t("Placemaker.UI.Painter.Theme." + id, POLISH_THEMES.find((x) => x.id === id)?.en ?? id);

const KIND_THEMES = [
    { value: "garden", icon: "flower" },
    { value: "natural", icon: "rocks" },
    { value: "tidy", icon: "topiary" },
    { value: "yard", icon: "crate" },
    { value: "off", icon: "eyeOff" },
];

const LOOK_DEFAULT: PolishLook = { painted: 33, lawns: true, planting: true, features: true, paths: true, rocks: true, groundPaint: true, shopPaving: true };

/** One options row, as Controls.tsx draws a type's: label (tooltip) left, editor right, reset when changed. */
const Row = ({ id, label, tip, toggle, changed, onReset, children }: { id: string; label: string; tip?: string; toggle?: boolean; changed?: boolean; onReset?: () => void; children: ReactNode }) => {
    const { t } = useT();
    return (
        <div className={c.row} data-testid={"polish-row-" + id}>
            <div className={c.rowHead}>
                <Tip title={label} description={tip || undefined}>
                    <div className={classNames(c.label, { [c.labelWide]: toggle })}>
                        <div className={c.labelText}>{label}</div>
                    </div>
                </Tip>
                <div className={classNames(c.editor, { [c.editorTight]: toggle })}>{children}</div>
                <div className={c.affordances}>
                    <div className={c.miniSpacer} />
                    {changed && onReset ? (
                        <Btn icon="reset" iconSize={11} className={c.mini} testId={"polish-reset-" + id} title={t("Placemaker.UI.ResetValue", "Reset to default")} onClick={onReset} />
                    ) : (
                        <div className={c.miniSpacer} />
                    )}
                </div>
            </div>
        </div>
    );
};

const ToggleRow = ({ id, label, tip, on, def = true, onChange }: { id: string; label: string; tip: string; on: boolean; def?: boolean; onChange: (v: boolean) => void }) => (
    <Row id={id} label={label} tip={tip} toggle changed={on !== def} onReset={() => onChange(def)}>
        <div className={c.toggleCell}>
            <Switch testId={"polish-" + id} on={on} onChange={onChange} />
        </div>
    </Row>
);

const Look = ({ look }: { look: PolishLook }) => {
    const { t } = useT();
    const set = (id: string, v: number | boolean) => act.polishOption(id, typeof v === "boolean" ? (v ? 1 : 0) : v);
    const parts = (["lawns", "planting", "features", "paths", "rocks", "groundPaint", "shopPaving"] as const).filter((k) => look[k]).length;
    const L = (id: string, en: string) => t("Placemaker.UI.Polish." + id, en);
    const tip = (id: string, en: string) => t("Placemaker.UI.Polish." + id + ".Tip", en);
    return (
        <Collapsible
            id="polish:look"
            title={t("Placemaker.UI.Polish.Look", "Look")}
            icon="brush"
            defaultOpen
            testId="polish-look"
            summary={t("Placemaker.UI.Polish.LookSummary", "{0}% painted · {1} of 7 parts", [look.painted, parts])}
        >
            <Row
                id="painted"
                label={L("Painted", "Painted gardens")}
                tip={tip("Painted", "How many gardens are painted (lush ground, soft edges) instead of keeping their lawns. Natural and Yard are painted more often, Tidy seldom; 0 never, 100 always. A mix looks most like a real street.")}
                changed={look.painted !== LOOK_DEFAULT.painted}
                onReset={() => set("painted", LOOK_DEFAULT.painted)}
            >
                <Slider testId="polish-painted" value={look.painted} min={0} max={100} step={5} unit="%" onPreview={() => {}} onCommit={(v) => set("painted", v)} />
            </Row>
            <ToggleRow id="lawns" label={L("Lawns", "Lawns up to the fences")} tip={tip("Lawns", "A garden's lawn grows to its new fences, around driveways and paving. Off: only fences and hedges are joined.")} on={look.lawns} onChange={(v) => set("lawns", v)} />
            <ToggleRow id="planting" label={L("Planting", "Beds and bushes")} tip={tip("Planting", "Planted beds with bushes along some of the new fences and hedges. Off: no planting, features, paths or rocks.")} on={look.planting} onChange={(v) => set("planting", v)} />
            <ToggleRow id="features" label={L("Features", "Garden features")} tip={tip("Features", "Big new garden land gets something now and then, by theme: a rock garden, a sand pit, a log pile, a bench nook, a flower garden, a storage yard.")} on={look.features} onChange={(v) => set("features", v)} />
            <ToggleRow id="paths" label={L("Paths", "Paths and walkways")} tip={tip("Paths", "A paved path through some big gardens, a walkway between some houses with a narrow gap.")} on={look.paths} onChange={(v) => set("paths", v)} />
            <ToggleRow id="rocks" label={L("Rocks", "Rocks")} tip={tip("Rocks", "Now and then a rock where two gardens meet at the street (never against a hedge).")} on={look.rocks} onChange={(v) => set("rocks", v)} />
            <ToggleRow id="groundPaint" label={L("GroundPaint", "Ground paint")} tip={tip("GroundPaint", "Soft lush ground past the gardens, blending them into the land around; worn ground along fences facing open land.")} on={look.groundPaint} onChange={(v) => set("groundPaint", v)} />
            <ToggleRow id="shopPaving" label={L("ShopPaving", "Shops keep their paving")} tip={tip("ShopPaving", "Shops, offices and works fill the land between them with the surface most of their ground already is, not lawn.")} on={look.shopPaving} onChange={(v) => set("shopPaving", v)} />
        </Collapsible>
    );
};

type Ask = "polishAll" | "unpolishAll" | null;

const City = ({ p, city }: { p: PainterState; city: PolishCity }) => {
    const { t } = useT();
    const [ask, setAsk] = useState<Ask>(null);
    const L = (id: string, en: string) => t("Placemaker.UI.Polish." + id, en);
    const status =
        t("Placemaker.UI.Polish.Count", "{0} of {1} buildings polished", [city.polished, city.total]) +
        (city.restoring > 0
            ? " · " + t("Placemaker.UI.Polish.Restoring", "putting {0} back", [city.restoring])
            : city.waiting > 0 && p.featureOn
              ? " · " + t("Placemaker.UI.Polish.Working", "working through {0}", [city.waiting])
              : "");
    const kinds: { id: keyof PolishCity; en: string }[] = [
        { id: "homes", en: "Homes" },
        { id: "shops", en: "Shops" },
        { id: "offices", en: "Offices" },
        { id: "industry", en: "Industry" },
    ];
    const kindOptions = KIND_THEMES.map((k) => ({
        value: k.value,
        icon: k.icon,
        title: k.value === "off" ? t("Placemaker.UI.Polish.KindOff", "Off") : themeName(t, k.value),
        description: k.value === "off" ? t("Placemaker.UI.Polish.KindOffTip", "New buildings of this kind are not polished") : t("Placemaker.UI.Painter.ThemeTip." + k.value, POLISH_THEMES.find((x) => x.id === k.value)?.tip ?? ""),
    }));
    const theme = p.theme === "zone" ? t("Placemaker.UI.Polish.EachKind", "each its kind's theme") : themeName(t, p.theme);
    return (
        <Collapsible id="polish:city" title={L("City", "Whole city")} icon="building" testId="polish-city" summary={status}>
            <div className={s.status} data-testid="polish-status" data-polished={city.polished} data-total={city.total} data-waiting={city.waiting}>
                <div className={s.swatch} />
                <div className={s.statusText}>{status}</div>
            </div>
            <ToggleRow
                id="on"
                label={L("On", "Neighbourhood polish")}
                tip={L("On.Tip", "On: polished gardens keep fitting as the city grows and changes. Off: nothing changes; polished gardens stay as they are. Polish switches it on.")}
                on={p.featureOn}
                def={p.featureOn}
                onChange={(v) => act.polishCity("on", v ? "true" : "false")}
            />
            <ToggleRow
                id="newHouses"
                label={L("NewHouses", "Polish new buildings")}
                tip={L("NewHouses.Tip", "Buildings that grow from now on are polished with their kind's theme below and the look above.")}
                on={p.newHouses}
                onChange={(v) => act.polishCity("newHouses", v ? "true" : "false")}
            />
            {kinds.map((k) => (
                <Row key={k.id} id={"kind-" + k.id} label={L("Kind." + k.id, k.en)} tip={L("Kind.Tip", "The theme new buildings of this kind get, and Polish with By kind. Off: new ones are not polished.")}>
                    <Segmented testId={"polish-kind-" + k.id} iconOnly options={kindOptions} value={String(city[k.id])} onChange={(v) => act.polishCity(k.id, v)} />
                </Row>
            ))}
            <div className={s.buttons}>
                <Btn variant="solid" icon="sparkle" iconSize={12} label={L("PolishAll", "Polish the whole city")} testId="polish-all" onClick={() => setAsk("polishAll")} />
                <Btn variant="solid" icon="reset" iconSize={12} label={L("UnpolishAll", "Unpolish the whole city")} testId="polish-none" disabled={city.polished === 0} onClick={() => setAsk("unpolishAll")} />
            </div>
            {ask ? (
                <Confirm
                    title={ask === "polishAll" ? L("PolishAll.Title", "Polish the whole city?") : L("UnpolishAll.Title", "Unpolish the whole city?")}
                    message={
                        ask === "polishAll"
                            ? t("Placemaker.UI.Polish.PolishAll.Message", "All {0} buildings get {1} and the look above (kinds set to Off are left out with By kind). It runs in the background a few buildings at a time, so the game keeps running smoothly; in a big city it takes a few minutes.", [city.total, theme])
                            : t("Placemaker.UI.Polish.UnpolishAll.Message", "All {0} polished gardens go back to how the game made them: fences, hedges and lawns back, the beds, paths, bushes and paving taken away (ground paint stays). It runs in the background a few hundred at a time.", [city.polished])
                    }
                    confirm={ask === "polishAll" ? L("PolishAll.Ok", "Polish all") : L("UnpolishAll.Ok", "Unpolish all")}
                    danger={ask === "unpolishAll"}
                    onConfirm={() => {
                        act.polishCity(ask);
                        setAsk(null);
                    }}
                    onCancel={() => setAsk(null)}
                />
            ) : null}
        </Collapsible>
    );
};

/**
 * The polish tool's Design (Placemaker's panel, step 2): its Style row as the Make row draws one (the themes as chips), then
 * Look as a type's options (rows under a heading), then Whole city folded at the bottom (the special settings).
 */
export const PolishDesign = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const p = state.painter;
    if (!p) return null;
    const color = categoryColor("neighbourhood");
    return (
        <div className={d.stage} data-testid="polish-design" data-theme={p.theme}>
            <div className={b.picker} data-testid="polish-style-picker" data-open="true">
                <div className={b.head}>
                    <div className={b.headToggle}>
                        <Icon name="chevronDown" size={11} tint="dim" />
                        <div className={b.headTitle}>{t("Placemaker.UI.Polish.PickStyle", "Pick a style")}</div>
                    </div>
                    <div className={b.flex} />
                </div>
                <div className={b.browser}>
                    <div className={b.row}>
                        <RowLabel n={1} label={t("Placemaker.UI.Make.Style", "Style")} />
                        <div className={b.chips} data-testid="polish-themes">
                            {POLISH_THEMES.map((th) => (
                                <Chip
                                    key={th.id}
                                    testId={"polish-theme-" + th.id}
                                    nav="preset"
                                    icon={th.icon}
                                    color={color}
                                    name={themeName(t, th.id)}
                                    on={p.theme === th.id}
                                    description={t("Placemaker.UI.Painter.ThemeTip." + th.id, th.tip)}
                                    onClick={() => act.polishTheme(th.id)}
                                />
                            ))}
                        </div>
                    </div>
                </div>
            </div>
            <div className={d.options} data-testid="options">
                {p.look ? <Look look={p.look} /> : null}
                {p.city ? <City p={p} city={p.city} /> : null}
            </div>
        </div>
    );
};

/** The polish tool's Review (step 3): what Polish does to the area, before it is confirmed with Polish (or Unpolish). */
export const PolishReview = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const p = state.painter;
    if (!p) return null;
    const look = p.look;
    const parts: { on: boolean; icon: string; key: string; en: string }[] = look
        ? [
              { on: look.lawns, icon: "leaf", key: "Lawns", en: "Lawns up to the fences" },
              { on: look.planting, icon: "shrub", key: "Planting", en: "Beds and bushes" },
              { on: look.features, icon: "bench", key: "Features", en: "Garden features" },
              { on: look.paths, icon: "footpath", key: "Paths", en: "Paths and walkways" },
              { on: look.rocks, icon: "rocks", key: "Rocks", en: "Rocks" },
              { on: look.groundPaint, icon: "paint", key: "GroundPaint", en: "Ground paint" },
              { on: look.shopPaving, icon: "shop", key: "ShopPaving", en: "Shops keep their paving" },
          ]
        : [];
    const theme = POLISH_THEMES.find((x) => x.id === p.theme) ?? POLISH_THEMES[0];
    return (
        <div className={r.stage} data-testid="polish-review">
            <div className={r.money} data-testid="polish-review-numbers">
                <div className={r.moneyRow}>
                    <div className={r.moneyLabel}>{t("Placemaker.UI.Polish.Buildings", "Buildings")}</div>
                    <div className={r.moneyValue} data-testid="polish-review-lots">{p.lots}</div>
                    <div className={r.moneyLabel}>{t("Placemaker.UI.Polish.AlreadyPolished", "Polished now")}</div>
                    <div className={r.moneyValue} data-testid="polish-review-polished">{p.polished}</div>
                    <div className={r.flex} />
                </div>
            </div>
            <div className={r.stats} data-testid="polish-review-stats">
                <Tip title={t("Placemaker.UI.Make.Style", "Style")} description={t("Placemaker.UI.Painter.ThemeTip." + theme.id, theme.tip)}>
                    <div className={r.stat} data-testid="polish-review-theme">
                        <Icon name={theme.icon} size={12} tint="dim" />
                        <div className={r.statValue}>{themeName(t, theme.id)}</div>
                        <div className={r.statLabel}>{t("Placemaker.UI.Polish.StyleWord", "style")}</div>
                    </div>
                </Tip>
                {look ? (
                    <div className={r.stat}>
                        <Icon name="paint" size={12} tint="dim" />
                        <div className={r.statValue}>{look.painted + "%"}</div>
                        <div className={r.statLabel}>{t("Placemaker.UI.Polish.PaintedWord", "painted gardens")}</div>
                    </div>
                ) : null}
                {parts.filter((x) => x.on).map((x) => (
                    <div key={x.key} className={r.stat}>
                        <Icon name={x.icon} size={12} tint="dim" />
                        <div className={r.statLabel}>{t("Placemaker.UI.Polish." + x.key, x.en).toLowerCase()}</div>
                    </div>
                ))}
            </div>
            {p.lots === 0 ? (
                <div className={s.off} data-testid="polish-review-none">
                    <Icon name="warning" size={12} tint="warn" />
                    <div className={s.offText}>{t("Placemaker.Painter.NoLots", "No buildings in this area")}</div>
                </div>
            ) : (
                <div className={r.allGood} data-testid="polish-review-note">
                    <Icon name="check" size={11} tint="ok" />
                    <div className={r.allGoodText}>
                        {t("Placemaker.UI.Polish.ReviewNote", "Polish gives them this style and look; each keeps it. Unpolish puts them back as the game made them.")}
                    </div>
                </div>
            )}
        </div>
    );
};
