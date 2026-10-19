// (PROTOCOL 25) The neighbourhood-polish tool's panel (its own button next to Placemaker's; Placemaker's panel and area tools).
// Step 2 "Polish" (PolishDesign): what the area holds, the look picker (By zone, the built-in looks, the player's own: like
// Placemaker's Style row), the look's buttons (Save, Save as new, Rename, Delete, Revert) and every setting of the look as
// rows (label left, control right, reset to the look's value), grouped and folded. Polish and Unpolish are the footer's
// buttons. The City window (PolishCityCard, beside the panel, its header button): the polish on or off, new buildings, a look
// for each zone type (and each kind), Polish / Unpolish the whole city (asking first, a few buildings a frame).
import React, { ReactNode, useState } from "react";
import classNames from "classnames";
import { PainterState, PolishZone, ToolState } from "../contract";
import { humanize, TFunc, useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { Btn, Collapsible, Icon, Segmented, Switch } from "./common";
import { Slider } from "../controls/Slider";
import { Confirm } from "./dialog";
import { act } from "../lib/bindings";
import { categoryColor, CUSTOM_COLOR } from "../lib/palette";
import { Chip, RowLabel } from "../stages/PresetBrowser";
import b from "../stages/browser.module.scss";
import d from "../stages/design.module.scss";
import c from "../controls/controls.module.scss";
import s from "./painter.module.scss";
import pn from "./panel.module.scss";

export const THEME_ICON: Record<string, string> = { zone: "zone", garden: "flower", natural: "rocks", tidy: "topiary", yard: "crate" };
const THEMES = ["garden", "natural", "tidy", "yard"];

/** A look's name: the player's own as they named it, a built-in one by its key. */
export const lookName = (t: TFunc, p: PainterState | undefined, id: string) => {
    if (id === "zone") return t("Placemaker.UI.Painter.Theme.zone", "By zone");
    if (id === "off") return t("Placemaker.UI.Polish.KindOff", "Off");
    const l = p?.looks?.find((x) => x.id === id);
    if (!l) return humanize(id);
    return l.custom ? l.name : t(l.name, humanize(l.id));
};

type Knob = { id: string; en: string; tip: string; kind: "toggle" | "slider"; min?: number; max?: number; step?: number; unit?: string };
const GROUPS: { id: string; en: string; icon: string; open?: boolean; knobs: Knob[] }[] = [
    {
        id: "reach", en: "Reach and joining", icon: "ruler", open: true,
        knobs: [
            { id: "sideReach", en: "Side reach", tip: "How far a garden reaches sideways to meet the neighbour beside it.", kind: "slider", min: 0, max: 200, step: 1, unit: "m" },
            { id: "maxFrontGap", en: "Street gap", tip: "Two neighbours this far apart at the street still join (one shared fence or hedge).", kind: "slider", min: 0, max: 200, step: 1, unit: "m" },
            { id: "maxBackGap", en: "Back gap", tip: "How far apart their back corners may be (wedges on the outside of a curve).", kind: "slider", min: 0, max: 300, step: 1, unit: "m" },
            { id: "maxRearGap", en: "Back to back", tip: "Two gardens back to back this far apart meet in the middle.", kind: "slider", min: 0, max: 200, step: 1, unit: "m" },
            { id: "maxFacingGap", en: "Block corners", tip: "A side facing another house's side or back this far away meets it halfway.", kind: "slider", min: 0, max: 150, step: 1, unit: "m" },
            { id: "maxDeepen", en: "Line up backs", tip: "A shallow garden reaches back to its row's deepest by up to this much.", kind: "slider", min: 0, max: 150, step: 1, unit: "m" },
            { id: "hug", en: "Reach out to roads and buildings", tip: "A side or back nobody else has grows out to a road, path or building near it and hugs it.", kind: "toggle" },
            { id: "hugReach", en: "Reach out", tip: "How far a garden reaches out to a road, path or building.", kind: "slider", min: 0, max: 150, step: 1, unit: "m" },
            { id: "wrap", en: "Meet behind a shallow lot", tip: "Two deep gardens meet behind a shallow one between them.", kind: "toggle" },
            { id: "closeBacks", en: "Close open backs", tip: "A garden open at the back closes with a fence or hedge the joining freed.", kind: "toggle" },
        ],
    },
    {
        id: "lawns", en: "Lawns and ground paint", icon: "leaf",
        knobs: [
            { id: "lawns", en: "Lawns up to the fences", tip: "A garden's lawn grows to its new fences, around driveways and paving.", kind: "toggle" },
            { id: "paintedGardens", en: "Painted gardens", tip: "How many gardens are painted (lush ground, soft edges) instead of keeping their lawns. Natural and Yard more often, Tidy seldom.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "gardenPaint", en: "Garden paint", tip: "How strong the paint of a painted garden is.", kind: "slider", min: 0, max: 1, step: 0.05 },
            { id: "paint", en: "Ground paint", tip: "Soft lush ground past the gardens and worn ground along fences facing open land.", kind: "toggle" },
            { id: "blendWidth", en: "Paint reach", tip: "How far the lush ground reaches past the gardens.", kind: "slider", min: 0, max: 80, step: 1, unit: "m" },
            { id: "blendFar", en: "Faint reach", tip: "How far the fainter, wider band reaches.", kind: "slider", min: 0, max: 120, step: 1, unit: "m" },
            { id: "blendStrength", en: "Paint strength", tip: "How strong the paint past the gardens is.", kind: "slider", min: 0, max: 1, step: 0.05 },
        ],
    },
    {
        id: "planting", en: "Planting and features", icon: "shrub",
        knobs: [
            { id: "plant", en: "Beds and bushes", tip: "Planted beds with bushes along new fences. Off: no planting, features, paths or rocks.", kind: "toggle" },
            { id: "bedChance", en: "Beds", tip: "How often a new fence gets a bed with bushes.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "bushesPerGarden", en: "Bushes", tip: "The most bushes the beds of one garden get.", kind: "slider", min: 0, max: 80, step: 1, unit: "count" },
            { id: "features", en: "Garden features", tip: "Big new garden land gets something by its theme: a rock garden, a sand pit, a log pile, a bench nook, a flower garden, a storage yard.", kind: "toggle" },
            { id: "featureChance", en: "Feature chance", tip: "How many big gardens get a feature.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "featureMinLand", en: "Feature land", tip: "New garden land a feature needs.", kind: "slider", min: 10, max: 600, step: 10, unit: "m2" },
            { id: "rockChance", en: "Rocks", tip: "How often a rock sits where two gardens meet at the street (never against a hedge).", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
        ],
    },
    {
        id: "paths", en: "Paths and shops", icon: "footpath",
        knobs: [
            { id: "pathChance", en: "Garden paths", tip: "How many big gardens get a paved path from the house to the back.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "alleys", en: "Walkways between houses", tip: "A narrow even gap between two houses becomes a paved walkway.", kind: "toggle" },
            { id: "alleyChance", en: "Walkways", tip: "How often a narrow gap becomes a walkway.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "pavedFill", en: "Shops keep their paving", tip: "Shops, offices and works fill the land between them with the surface most of their ground already is, not lawn.", kind: "toggle" },
        ],
    },
];

const changedIn = (knobs: Knob[], p: PainterState) => knobs.filter((k) => Math.abs((p.values?.[k.id] ?? 0) - (p.lookValues?.[k.id] ?? 0)) > 1e-4).length;

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

const KnobRow = ({ k, p }: { k: Knob; p: PainterState }) => {
    const { t } = useT();
    const v = p.values?.[k.id] ?? 0;
    const def = p.lookValues?.[k.id] ?? v;
    const changed = Math.abs(v - def) > 1e-4;
    const label = t("Placemaker.UI.Polish.Knob." + k.id, k.en);
    const tip = t("Placemaker.UI.Polish.Knob." + k.id + ".Tip", k.tip);
    const set = (x: number) => act.polishKnob(k.id, x);
    if (k.kind === "toggle")
        return (
            <Row id={k.id} label={label} tip={tip} toggle changed={changed} onReset={() => set(def)}>
                <div className={c.toggleCell}>
                    <Switch testId={"polish-" + k.id} on={v > 0.5} onChange={(on) => set(on ? 1 : 0)} />
                </div>
            </Row>
        );
    return (
        <Row id={k.id} label={label} tip={tip} changed={changed} onReset={() => set(def)}>
            <Slider testId={"polish-" + k.id} value={v} min={k.min ?? 0} max={k.max ?? 100} step={k.step ?? 1} unit={k.unit} onPreview={() => {}} onCommit={set} />
        </Row>
    );
};

/** The look's buttons: Save (the player's own), Save as new, Rename, Delete, Revert. A name field when one is asked for. */
const LookBar = ({ p }: { p: PainterState }) => {
    const { t } = useT();
    const [naming, setNaming] = useState<"saveAs" | "rename" | null>(null);
    const [name, setName] = useState("");
    const [asking, setAsking] = useState(false);
    const look = p.looks?.find((x) => x.id === p.theme);
    if (!look) return null;
    const submit = () => {
        if (naming && name.trim()) act.polishLook(naming, name.trim());
        setNaming(null);
    };
    if (naming)
        return (
            <div className={s.lookBar} data-testid="polish-look-name">
                <input
                    className={b.searchInput}
                    data-testid="polish-look-name-input"
                    autoFocus
                    value={name}
                    placeholder={t("Placemaker.UI.Polish.LookName", "Name of the look")}
                    onChange={(e) => setName(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter") submit();
                        else if (e.key === "Escape") setNaming(null);
                        e.stopPropagation();
                    }}
                />
                <Btn variant="accent" label={t("Placemaker.UI.Polish.SaveName", "Save")} onClick={submit} testId="polish-look-name-ok" />
                <Btn label={t("Placemaker.UI.Cancel", "Cancel")} onClick={() => setNaming(null)} testId="polish-look-name-cancel" />
            </div>
        );
    return (
        <div className={s.lookBar} data-testid="polish-look-bar">
            {p.modified ? <div className={s.modified}>{t("Placemaker.UI.Polish.Changed", "Changed")}</div> : null}
            <div className={s.flex} />
            {look.custom && p.modified ? <Btn variant="accent" icon="save" iconSize={12} label={t("Placemaker.UI.Polish.Save", "Save")} onClick={() => act.polishLook("save")} testId="polish-look-save" /> : null}
            <Btn
                icon="plus"
                iconSize={12}
                label={t("Placemaker.UI.Polish.SaveAs", "Save as new")}
                onClick={() => {
                    setName(look.custom ? look.name + " 2" : "");
                    setNaming("saveAs");
                }}
                testId="polish-look-saveas"
            />
            {p.modified ? <Btn icon="undo" iconSize={12} title={t("Placemaker.UI.Polish.Revert", "Back to the saved look")} onClick={() => act.polishLook("revert")} testId="polish-look-revert" /> : null}
            {look.custom ? (
                <Btn
                    icon="edit"
                    iconSize={12}
                    title={t("Placemaker.UI.Polish.Rename", "Rename")}
                    onClick={() => {
                        setName(look.name);
                        setNaming("rename");
                    }}
                    testId="polish-look-rename"
                />
            ) : null}
            {look.custom ? <Btn icon="trash" iconSize={12} title={t("Placemaker.UI.Polish.Delete", "Delete this look")} onClick={() => setAsking(true)} testId="polish-look-delete" /> : null}
            {asking ? (
                <Confirm
                    title={t("Placemaker.UI.Polish.DeleteTitle", "Delete this look?")}
                    message={t("Placemaker.UI.Polish.DeleteMessage", "Buildings polished with “{0}” keep how they look until they are polished again; zones that used it follow their kind.", [look.name])}
                    confirm={t("Placemaker.UI.Polish.DeleteOk", "Delete")}
                    danger
                    onConfirm={() => {
                        act.polishLook("delete");
                        setAsking(false);
                    }}
                    onCancel={() => setAsking(false)}
                />
            ) : null}
        </div>
    );
};

/** Step 2 of the polish tool: what the area holds, the look picker and every setting of the look. */
export const PolishDesign = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const p = state.painter;
    if (!p) return null;
    const color = categoryColor("neighbourhood");
    const hasArea = state.area.closed && state.area.areaM2 > 0;
    const looks = p.looks ?? [];
    const byZone = p.theme === "zone";
    return (
        <div className={d.stage} data-testid="polish-design" data-theme={p.theme}>
            <div className={s.count} data-testid="painter-count" data-lots={p.lots} data-polished={p.polished}>
                <div className={s.swatch} />
                <div>
                    {hasArea
                        ? t("Placemaker.UI.Painter.Count", "{0} buildings in the area · {1} polished", [p.lots, p.polished])
                        : t("Placemaker.UI.Painter.Legend", "Green: polished buildings")}
                </div>
            </div>
            <div className={b.picker} data-testid="polish-style-picker" data-open="true">
                <div className={b.head}>
                    <div className={b.headToggle}>
                        <Icon name="chevronDown" size={11} tint="dim" />
                        <div className={b.headTitle}>{t("Placemaker.UI.Polish.PickLook", "Pick a look")}</div>
                    </div>
                    <div className={b.flex} />
                </div>
                <div className={b.browser}>
                    <div className={b.row}>
                        <RowLabel n={1} label={t("Placemaker.UI.Polish.Look", "Look")} />
                        <div className={b.chips} data-testid="polish-themes">
                            <Chip
                                testId="polish-theme-zone"
                                nav="preset"
                                icon="zone"
                                color={color}
                                name={lookName(t, p, "zone")}
                                on={byZone}
                                description={t("Placemaker.UI.Painter.ThemeTip.zone", "Each building gets its zone's look (City)")}
                                onClick={() => act.polishTheme("zone")}
                            />
                            {looks.map((l) => (
                                <Chip
                                    key={l.id}
                                    testId={"polish-theme-" + l.id}
                                    nav="preset"
                                    icon={l.custom ? "star" : THEME_ICON[l.theme] ?? "flower"}
                                    color={l.custom ? CUSTOM_COLOR : color}
                                    name={lookName(t, p, l.id)}
                                    on={p.theme === l.id}
                                    description={l.custom ? t("Placemaker.UI.Polish.YourLook", "Your look, based on {0}", [lookName(t, p, l.theme)]) : t("Placemaker.UI.Painter.ThemeTip." + l.id, "")}
                                    badge={p.theme === l.id && p.modified ? <div className={b.dot} /> : null}
                                    onClick={() => act.polishTheme(l.id)}
                                />
                            ))}
                        </div>
                    </div>
                </div>
            </div>
            {byZone ? (
                <div className={s.off} data-testid="polish-byzone-note">
                    <Icon name="info" size={12} tint="dim" />
                    <div className={s.offText}>
                        {t("Placemaker.UI.Polish.ByZoneNote", "Each building gets the look its zone has. Set them in City (the button at the top), or pick a look here to polish the area with it.")}
                    </div>
                </div>
            ) : (
                <div className={d.options} data-testid="options">
                    <LookBar p={p} />
                    <Row
                        id="theme"
                        label={t("Placemaker.UI.Polish.BaseStyle", "Base style")}
                        tip={t("Placemaker.UI.Polish.BaseStyleTip", "What kind of planting and features the look has.")}
                        changed={p.lookTheme !== p.lookSavedTheme}
                        onReset={() => act.polishKnob("theme", THEMES.indexOf(p.lookSavedTheme))}
                    >
                        <Segmented
                            testId="polish-base"
                            iconOnly
                            value={p.lookTheme}
                            onChange={(v) => act.polishKnob("theme", THEMES.indexOf(v))}
                            options={THEMES.map((th) => ({ value: th, icon: THEME_ICON[th], title: lookName(t, p, th), description: t("Placemaker.UI.Painter.ThemeTip." + th, "") }))}
                        />
                    </Row>
                    {GROUPS.map((g) => (
                        <Collapsible
                            key={g.id}
                            id={"polish:" + g.id}
                            title={t("Placemaker.UI.Polish.Group." + g.id, g.en)}
                            icon={g.icon}
                            defaultOpen={!!g.open}
                            testId={"polish-group-" + g.id}
                            summary={changedIn(g.knobs, p) > 0 ? t("Placemaker.UI.Polish.GroupChanged", "{0} changed", [changedIn(g.knobs, p)]) : undefined}
                        >
                            {g.knobs.map((k) => (
                                <KnobRow key={k.id} k={k} p={p} />
                            ))}
                        </Collapsible>
                    ))}
                </div>
            )}
        </div>
    );
};

/** The looks to pick from (a short list of chips): Off, every look, and "as its kind" for a zone with its own. */
const LookChips = ({ p, value, testId, onPick, inherit }: { p: PainterState; value: string; testId: string; onPick: (v: string) => void; inherit?: boolean }) => {
    const { t } = useT();
    const options = [...(p.looks ?? []).map((l) => l.id), "off"];
    return (
        <div className={s.choiceList} data-testid={testId}>
            {inherit ? (
                <button type="button" className={s.choiceItem} data-testid={testId + "-inherit"} onClick={(e) => { e.stopPropagation(); onPick(""); }}>
                    <Icon name="undo" size={10} tint="dim" />
                    <div className={s.choiceText}>{t("Placemaker.UI.Polish.AsKind", "As its kind")}</div>
                </button>
            ) : null}
            {options.map((o) => (
                <button
                    key={o}
                    type="button"
                    className={classNames(s.choiceItem, { [s.choiceItemOn]: o === value })}
                    data-testid={testId + "-" + o}
                    onClick={(e) => {
                        e.stopPropagation();
                        onPick(o);
                    }}
                >
                    <Icon name={o === "off" ? "eyeOff" : lookIcon(p, o)} size={10} tint={o === value ? "light" : "dim"} />
                    <div className={s.choiceText}>{lookName(t, p, o)}</div>
                </button>
            ))}
        </div>
    );
};

const lookIcon = (p: PainterState, id: string) => {
    const l = (p.looks ?? []).find((x) => x.id === id);
    return !l ? "eyeOff" : l.custom ? "star" : THEME_ICON[l.theme] ?? "flower";
};

const KINDS: { id: "home" | "shop" | "office" | "industry"; city: "homes" | "shops" | "offices" | "industry"; en: string; icon: string }[] = [
    { id: "home", city: "homes", en: "Homes", icon: "people" },
    { id: "shop", city: "shops", en: "Shops", icon: "shop" },
    { id: "office", city: "offices", en: "Offices", icon: "building" },
    { id: "industry", city: "industry", en: "Industry", icon: "factory" },
];

/** A zone's region (the prefab name's EU / NA) and its name without it. */
function zoneLabel(t: TFunc, id: string): { region: string; name: string } {
    const m = /^(EU|NA)\s+(.*)$/.exec(id);
    const region = m ? m[1] : "";
    const local = t("Assets.NAME[" + id + "]", "");
    const name = local && local !== id ? local.replace(/^(EU|NA)\s+/, "") : humanize(m ? m[2] : id);
    return { region, name };
}

/** One zone type as the zoning toolbar shows it: its icon, its name, its look underneath (dim: its kind's). */
const ZoneTile = ({ p, z, on, onClick }: { p: PainterState; z: PolishZone; on: boolean; onClick: () => void }) => {
    const { t } = useT();
    const { region, name } = zoneLabel(t, z.id);
    return (
        <Tip title={name} description={(region ? region + " · " : "") + t("Placemaker.UI.Polish.ZoneTip", "Click to pick its look")}>
            <button
                type="button"
                className={classNames(s.zoneTile, { [s.zoneTileOn]: on })}
                data-testid={"polish-zone-" + z.id}
                data-look={z.look}
                onClick={(e) => {
                    e.stopPropagation();
                    onClick();
                }}
            >
                <div className={s.zoneTop}>
                    <Icon name={z.icon || "zone"} size={22} fallback="zone" />
                    {region ? <div className={s.regionTag}>{region}</div> : null}
                </div>
                <div className={s.zoneName}>{name}</div>
                <div className={classNames(s.zoneLook, { [s.zoneLookOwn]: z.own })}>
                    <Icon name={z.look === "off" ? "eyeOff" : lookIcon(p, z.look)} size={9} tint={z.own ? "light" : "dim"} />
                    <div className={s.zoneLookText}>{lookName(t, p, z.look)}</div>
                </div>
            </button>
        </Tip>
    );
};

type Ask = "polishAll" | "unpolishAll" | null;

/** The City window beside the panel (Placemaker's frame): new buildings, a look per zone type, the whole city. */
export const PolishCityCard = ({ state, onClose }: { state: ToolState; onClose: () => void }) => {
    const { t } = useT();
    const [ask, setAsk] = useState<Ask>(null);
    const [picking, setPicking] = useState<string | null>(null);   // a zone id, or "kind:<kind>"
    const p = state.painter;
    if (!p || !p.city) return null;
    const city = p.city;
    const L = (id: string, en: string) => t("Placemaker.UI.Polish." + id, en);
    const status =
        t("Placemaker.UI.Polish.Count", "{0} of {1} buildings polished", [city.polished, city.total]) +
        (city.restoring > 0 ? " · " + t("Placemaker.UI.Polish.Restoring", "putting {0} back", [city.restoring]) : city.waiting > 0 ? " · " + t("Placemaker.UI.Polish.Working", "working through {0}", [city.waiting]) : "");
    const zones: PolishZone[] = p.zones ?? [];
    return (
        <div className={classNames(pn.panel, s.cityPanel)} data-testid="polish-city" onMouseDown={(e) => e.stopPropagation()}>
            <div className={pn.panelTint}>
                <div className={pn.header}>
                    <div className={pn.logo} style={{ marginLeft: "6rem" }}>
                        <Icon name="building" size={15} tint="accent" />
                    </div>
                    <div className={pn.title}>{L("City", "Whole city")}</div>
                    <div className={pn.flex} />
                    <Btn icon="close" iconSize={14} title={t("Placemaker.UI.Close", "Close")} onClick={onClose} testId="polish-city-close" />
                </div>
                <div className={pn.bodyWrap}>
                    <div className={classNames(pn.body, s.cityBody)}>
                        <Row id="newHouses" label={L("NewHouses", "Polish new buildings")} tip={L("NewHouses.Tip2", "Buildings that grow from now on get their zone's look. Buildings already standing stay as they are until you polish them.")} toggle>
                            <div className={c.toggleCell}>
                                <Switch testId="polish-newHouses" on={p.newHouses} onChange={(v) => act.polishCity("newHouses", v ? "true" : "false")} />
                            </div>
                        </Row>
                        <div className={s.status} data-testid="polish-status" data-polished={city.polished} data-total={city.total} data-waiting={city.waiting}>
                            <div className={s.swatch} />
                            <div className={s.statusText}>{status}</div>
                        </div>
                        {KINDS.map((k) => {
                            const list = zones.filter((z) => z.kind === k.id);
                            if (!list.length) return null;
                            const kindLook = String(city[k.city]);
                            const kindKey = "kind:" + k.id;
                            const picked = list.find((z) => z.id === picking);
                            return (
                                <Collapsible
                                    key={k.id}
                                    id={"polish:zones:" + k.id}
                                    title={t("Placemaker.UI.Polish.Kind." + k.city, k.en)}
                                    icon={k.icon}
                                    defaultOpen={k.id === "home"}
                                    testId={"polish-zones-" + k.id}
                                    summary={lookName(t, p, kindLook)}
                                    right={
                                        <button
                                            type="button"
                                            className={classNames(s.kindLook, { [s.kindLookOn]: picking === kindKey })}
                                            data-testid={"polish-kind-" + k.city}
                                            onClick={(e) => {
                                                e.stopPropagation();
                                                setPicking(picking === kindKey ? null : kindKey);
                                            }}
                                        >
                                            <Icon name={kindLook === "off" ? "eyeOff" : lookIcon(p, kindLook)} size={10} tint="light" />
                                            <div className={s.choiceText}>{lookName(t, p, kindLook)}</div>
                                            <Icon name="chevronDown" size={9} tint="dim" />
                                        </button>
                                    }
                                >
                                    {picking === kindKey ? (
                                        <div className={s.pickBox}>
                                            <div className={s.pickTitle}>{t("Placemaker.UI.Polish.KindPick", "Every {0} zone without its own look", [t("Placemaker.UI.Polish.Kind." + k.city, k.en).toLowerCase()])}</div>
                                            <LookChips p={p} value={kindLook} testId={"polish-kind-" + k.city + "-pick"} onPick={(v) => { if (v) act.polishCity(k.city, v); setPicking(null); }} />
                                        </div>
                                    ) : null}
                                    <div className={s.zoneGrid}>
                                        {list.map((z) => (
                                            <ZoneTile key={z.id} p={p} z={z} on={picking === z.id} onClick={() => setPicking(picking === z.id ? null : z.id)} />
                                        ))}
                                    </div>
                                    {picked ? (
                                        <div className={s.pickBox}>
                                            <div className={s.pickTitle}>{zoneLabel(t, picked.id).name + (zoneLabel(t, picked.id).region ? " · " + zoneLabel(t, picked.id).region : "")}</div>
                                            <LookChips p={p} value={picked.look} inherit={picked.own} testId={"polish-zone-" + picked.id + "-pick"} onPick={(v) => { act.polishZone(picked.id, v); setPicking(null); }} />
                                        </div>
                                    ) : null}
                                </Collapsible>
                            );
                        })}
                    </div>
                </div>
                <div className={s.cityFoot}>
                    <Btn variant="solid" icon="sparkle" iconSize={12} label={L("PolishAllShort", "Polish all")} title={L("PolishAll", "Polish the whole city")} testId="polish-all" onClick={() => setAsk("polishAll")} />
                    <div className={s.flex} />
                    <Btn variant="ghost" icon="reset" iconSize={12} label={L("UnpolishAllShort", "Unpolish all")} title={L("UnpolishAll", "Unpolish the whole city")} testId="polish-none" disabled={city.polished === 0} onClick={() => setAsk("unpolishAll")} />
                </div>
            </div>
            {ask ? (
                <Confirm
                    title={ask === "polishAll" ? L("PolishAll.Title", "Polish the whole city?") : L("UnpolishAll.Title", "Unpolish the whole city?")}
                    message={
                        ask === "polishAll"
                            ? t("Placemaker.UI.Polish.PolishAll.ByZone", "All {0} buildings get their zone's look (zones set to Off are left out). It runs in the background a few buildings at a time, so the game keeps running smoothly; in a big city it takes a few minutes.", [city.total])
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
        </div>
    );
};
