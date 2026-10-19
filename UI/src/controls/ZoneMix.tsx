// The Zoning types' Zoning tool (Core/Layers/Types/Zoning/PolishLookLayer; the Design page's Zoning section) as one editor
// instead of a row per setting: a bar of the mix, one line per zone (its colour and icon, its type from a short menu or an
// exact zone of the game, its share) with its lots' width and depth ranges, "Mix in another zone", then the mix pattern and
// the road rule. It writes the same settings the rows would
// (zoneA/B/C, zoneAssetA/B/C, shareA/B/C, …), so styles, undo, locks and Randomize keep working.
import React, { ReactNode, useRef, useState } from "react";
import classNames from "classnames";
import { LayerType, SettingValue, StepView } from "../contract";
import { act, playSound } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { ControlEx, valueOf } from "../lib/schema";
import { Icon } from "../components/common";
import { useAssetList, usePicker } from "../components/picker";
import { Slider } from "./Slider";
import z from "./zoneMix.module.scss";

const SLOTS = ["A", "B", "C"] as const;
type Slot = (typeof SLOTS)[number];

/** The zone tool's colours, by zone type. */
export const ZONE_COLOR: Record<string, string> = {
    residentialLow: "#9be07a",
    residentialRow: "#7fcf62",
    residentialMedium: "#5fb84a",
    residentialHigh: "#3f9a3a",
    mixed: "#52c7a6",
    lowRent: "#b9d35e",
    commercialLow: "#71b9ff",
    commercialHigh: "#3d86e6",
    officeLow: "#b896ff",
    officeHigh: "#8c66e6",
    industrial: "#f2b84b",
};

/** The game's zone of each type (Core/Layers/Common/ZoneKinds.PrefabId, without EU / NA). */
const KIND_ZONE: Record<string, string> = {
    residentialLow: "Residential Low",
    residentialRow: "Residential Medium Row",
    residentialMedium: "Residential Medium",
    residentialHigh: "Residential High",
    mixed: "Residential Mixed",
    lowRent: "Residential LowRent",
    commercialLow: "Commercial Low",
    commercialHigh: "Commercial High",
    officeLow: "Office Low",
    officeHigh: "Office High",
    industrial: "Industrial Manufacturing",
};

const KIND_GROUPS: { id: string; en: string; kinds: string[] }[] = [
    { id: "homes", en: "Homes", kinds: ["residentialLow", "residentialRow", "residentialMedium", "residentialHigh", "mixed", "lowRent"] },
    { id: "shops", en: "Shops", kinds: ["commercialLow", "commercialHigh"] },
    { id: "offices", en: "Offices", kinds: ["officeLow", "officeHigh"] },
    { id: "industry", en: "Industry", kinds: ["industrial"] },
];

/** A layer whose "zone" group this editor draws. */
export const isZoneMix = (layer: LayerType) => {
    const ids = new Set(layer.controls.map((c) => c.id));
    return ids.has("zoneArea") && ids.has("shareA") && ids.has("zoneA");
};

type Ctl = (id: string) => ControlEx | undefined;

function reader(step: StepView, layer: LayerType) {
    const ctl: Ctl = (id) => layer.controls.find((c) => c.id === id) as ControlEx | undefined;
    const val = (id: string): SettingValue | undefined => {
        const c = ctl(id);
        return c ? valueOf(step, c) : undefined;
    };
    const num = (id: string, d: number) => {
        const v = Number(val(id));
        return Number.isFinite(v) ? Math.round(v) : d;
    };
    const slots = SLOTS.map((sl) => ({
        sl,
        w0: num("widthMin" + sl, 2),
        w1: num("widthMax" + sl, 4),
        d0: num("depthMin" + sl, 2),
        d1: num("depthMax" + sl, 4),
        kind: String(val("zone" + sl) ?? ""),
        asset: typeof val("zoneAsset" + sl) === "string" ? (val("zoneAsset" + sl) as string) : "",
        share: Math.max(0, Number(val("share" + sl)) || 0),
    }));
    return { ctl, val, slots, used: slots.filter((x) => x.share > 0) };
}

const kindLabel = (t: TFunc, ctl: Ctl, kind: string) => {
    const o = (ctl("zoneA")?.options ?? []).find((x) => x.value === kind);
    return o ? t(o.label, humanize(o.value)) : humanize(kind);
};

/** The folded group's line: "Low housing 70% · Low commercial 30%", or Off. */
export function zoneMixSummary(step: StepView, layer: LayerType, t: TFunc, assetName: (c: string | undefined, id: string) => string): string {
    const { ctl, val, used } = reader(step, layer);
    if (val("zoneArea") !== true) return t("Placemaker.UI.Off", "Off");
    const total = used.reduce((n, x) => n + x.share, 0) || 1;
    const fill = Math.min(100, Math.max(5, Number(val("zoneFill")) || 100));
    return used.map((x) => (x.asset ? assetName("zone", x.asset) : kindLabel(t, ctl, x.kind)) + " " + Math.round((x.share / total) * fill) + "%").join(" · ");
}

/** A range of whole numbers on one track with two handles (the nearer handle follows the mouse); the value beside it. */
const RangeSlider = ({ lo, hi, min, max, testId, onCommit }: { lo: number; hi: number; min: number; max: number; testId: string; onCommit: (lo: number, hi: number) => void }) => {
    const [drag, setDrag] = useState<[number, number] | null>(null);
    const trackRef = useRef<HTMLDivElement>(null);
    const [a, b] = drag ?? [lo, hi];
    const pct = (v: number) => ((v - min) / (max - min || 1)) * 100;
    const fromX = (x: number) => {
        const r = trackRef.current?.getBoundingClientRect();
        if (!r || r.width <= 0) return a;
        const ratio = Math.min(1, Math.max(0, (x - r.left) / r.width));
        return Math.round(min + ratio * (max - min));
    };
    const onDown = (e: React.MouseEvent) => {
        if (e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        const v0 = fromX(e.clientX);
        const da = Math.abs(v0 - a), db = Math.abs(v0 - b);
        const which = da < db ? 0 : da > db ? 1 : v0 > b ? 1 : 0;
        let cur: [number, number] = [a, b];
        const apply = (v: number) => {
            cur = which === 0 ? [Math.min(v, cur[1]), cur[1]] : [cur[0], Math.max(v, cur[0])];
            setDrag(cur);
        };
        apply(v0);
        playSound("drag-slider");
        const move = (ev: MouseEvent) => apply(fromX(ev.clientX));
        const up = () => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            setDrag(null);
            if (cur[0] !== lo || cur[1] !== hi) onCommit(cur[0], cur[1]);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };
    return (
        <div className={z.rangeWrap} data-testid={testId} data-lo={a} data-hi={b}>
            <div className={z.rangeTrack} ref={trackRef} onMouseDown={onDown}>
                <div className={z.rangeRail}>
                    <div className={z.rangeFill} style={{ left: pct(a) + "%", width: Math.max(0, pct(b) - pct(a)) + "%" }} />
                </div>
                <div className={z.rangeThumb} style={{ left: pct(a) + "%" }} />
                <div className={z.rangeThumb} style={{ left: pct(b) + "%" }} />
            </div>
            <div className={z.value}>{a === b ? String(a) : a + "\u2013" + b}</div>
        </div>
    );
};

export const ZoneMix = ({ step, layer, rows }: { step: StepView; layer: LayerType; rows: (ids: string[]) => ReactNode }) => {
    const { t } = useT();
    const picker = usePicker();
    const zones = useAssetList("zone");
    const [menu, setMenu] = useState<string | null>(null);   // "A" | "B" | "C" | "road"
    const { ctl, val, slots, used } = reader(step, layer);
    const set = (id: string, v: SettingValue) => act.setValue(step.path, id, v);
    const on = val("zoneArea") === true;
    const total = used.reduce((n, x) => n + x.share, 0) || 1;

    /** The game's zone of a type (the city's EU or NA one when both exist). */
    const zoneOf = (kind: string, asset: string) => {
        if (asset) return zones?.find((a) => a.id === asset);
        const name = KIND_ZONE[kind];
        if (!name || !zones) return undefined;
        return zones.find((a) => a.id === name) ?? zones.find((a) => a.id.endsWith(" " + name));
    };
    const nameOf = (kind: string, asset: string) => (asset ? zoneOf(kind, asset)?.name || humanize(asset) : kindLabel(t, ctl, kind));
    const colourOf = (kind: string, asset: string) => (asset ? "#c9d3da" : ZONE_COLOR[kind] ?? "#c9d3da");

    /** Shares that always add up to 100: the one changed keeps its value, the others share the rest as they did. */
    const reshare = (fixed: Slot | null, fixedValue: number, keep: Slot[]) => {
        const cur = (sl: Slot) => slots.find((x) => x.sl === sl)?.share ?? 0;
        const others = keep.filter((sl) => sl !== fixed);
        const rest = 100 - (fixed ? fixedValue : 0);
        const was = others.reduce((n, sl) => n + cur(sl), 0);
        const next: Record<string, number> = {};
        let given = 0;
        others.forEach((sl, i) => {
            const left = others.length - 1 - i;   // zones still to give a share: 5 % each at least
            const w = was > 0 ? cur(sl) / was : 1 / others.length;
            let v = i === others.length - 1 ? rest - given : Math.round((rest * w) / 5) * 5;
            v = Math.max(5, Math.min(v, rest - given - 5 * left));
            next[sl] = v;
            given += v;
        });
        if (fixed) next[fixed] = fixedValue;
        for (const sl of SLOTS) {
            const v = keep.includes(sl) ? next[sl] ?? 0 : 0;
            if (v !== cur(sl)) set("share" + sl, v);
        }
    };

    const addZone = () => {
        const free = slots.find((x) => x.share <= 0);
        if (!free) return;
        const usedKinds = new Set(used.map((x) => x.kind));
        if (usedKinds.has(free.kind)) {
            const kind = KIND_GROUPS.flatMap((g) => g.kinds).find((k) => !usedKinds.has(k));
            if (kind) set("zone" + free.sl, kind);
        }
        const keep = [...used.map((x) => x.sl), free.sl];
        reshare(free.sl, Math.round(100 / keep.length / 5) * 5, keep);
        setMenu(free.sl);
    };

    const removeZone = (sl: Slot) => {
        const keep = used.map((x) => x.sl).filter((s) => s !== sl);
        if (!keep.length) return;
        reshare(null, 0, keep);
        if (menu === sl) setMenu(null);
    };

    const pickKind = (key: string, kind: string) => {
        if (key === "road") set("roadZone", kind);
        else {
            set("zone" + key, kind);
            if (slots.find((x) => x.sl === key)?.asset) set("zoneAsset" + key, "");
        }
        setMenu(null);
    };

    const pickExact = (sl: Slot) => {
        const cur = slots.find((x) => x.sl === sl);
        picker.open({
            category: "zone",
            title: t("Placemaker.Polish.zoneAsset" + sl, "Exact zone"),
            mode: "single",
            selected: cur?.asset ? [cur.asset] : [],
            allowAutomatic: true,
            onPick: (ids) => set("zoneAsset" + sl, ids[0] ?? ""),
        });
        setMenu(null);
    };

    /** The zone's button: its colour, its icon, its name, a chevron; opens the menu under the line. */
    const ZoneButton = ({ id, kind, asset }: { id: string; kind: string; asset: string }) => {
        const zone = zoneOf(kind, asset);
        return (
            <button
                type="button"
                className={classNames(z.zoneButton, { [z.zoneButtonOn]: menu === id })}
                data-testid={"zonemix-pick-" + id}
                onClick={(e) => {
                    e.stopPropagation();
                    setMenu(menu === id ? null : id);
                }}
            >
                <div className={z.swatch} style={{ backgroundColor: colourOf(kind, asset) }} />
                {zone?.icon ? <Icon name={zone.icon} size={14} fallback="zone" /> : <Icon name="zone" size={14} tint="dim" />}
                <div className={z.zoneName}>{nameOf(kind, asset)}</div>
                {asset ? <div className={z.exactTag}>{t("Placemaker.UI.ZoneMix.Exact", "exact")}</div> : null}
                <Icon name="chevronDown" size={9} tint="dim" />
            </button>
        );
    };

    const Menu = ({ id, kind }: { id: string; kind: string }) => (
        <div className={z.menu} data-testid={"zonemix-menu-" + id}>
            {KIND_GROUPS.map((g) => (
                <div key={g.id} className={z.menuGroup}>
                    <div className={z.menuTitle}>{t("Placemaker.UI.ZoneMix.Group." + g.id, g.en)}</div>
                    <div className={z.menuChips}>
                        {g.kinds.map((k) => {
                            const zone = zoneOf(k, "");
                            return (
                                <button
                                    key={k}
                                    type="button"
                                    className={classNames(z.chip, { [z.chipOn]: k === kind })}
                                    data-testid={"zonemix-" + id + "-" + k}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        pickKind(id, k);
                                    }}
                                >
                                    <div className={z.swatch} style={{ backgroundColor: ZONE_COLOR[k] }} />
                                    {zone?.icon ? <Icon name={zone.icon} size={12} fallback="zone" /> : null}
                                    <div className={z.chipText}>{kindLabel(t, ctl, k)}</div>
                                </button>
                            );
                        })}
                    </div>
                </div>
            ))}
            {id !== "road" ? (
                <button
                    type="button"
                    className={z.exactButton}
                    data-testid={"zonemix-" + id + "-exact"}
                    onClick={(e) => {
                        e.stopPropagation();
                        pickExact(id as Slot);
                    }}
                >
                    <Icon name="search" size={10} tint="dim" />
                    <div className={z.chipText}>{t("Placemaker.UI.ZoneMix.ExactPick", "Pick an exact zone (mods' too)…")}</div>
                </button>
            ) : null}
        </div>
    );

    const fill = Math.min(100, Math.max(5, Number(val("zoneFill")) || 100));
    /** A line's second and third rows: a label under the zone button, a control lined up with the share slider. */
    const Sub = ({ label, tip, children }: { label: string; tip: string; children: ReactNode }) => (
        <div className={z.sub} title={tip}>
            <div className={z.subLabel}>{label}</div>
            <div className={z.cell}>{children}</div>
            <div className={z.endSpacer} />
        </div>
    );

    return (
        <div className={z.mix} data-testid="zonemix">
            {rows(["zoneArea"])}
            {on ? (
                <>
                    {/* the mix as one bar: each zone's part of the land, labelled, and the land left empty */}
                    <div className={z.bar} data-testid="zonemix-bar">
                        {used.map((x) => {
                            const p = Math.round((x.share / total) * fill);
                            return (
                                <div key={x.sl} className={z.barPart} style={{ width: (x.share / total) * fill + "%", backgroundColor: colourOf(x.kind, x.asset) }}>
                                    <div className={z.barText}>{nameOf(x.kind, x.asset) + " " + p + "%"}</div>
                                </div>
                            );
                        })}
                        {fill < 100 ? (
                            <div className={classNames(z.barPart, z.barEmpty)} style={{ width: 100 - fill + "%" }}>
                                <div className={z.barText}>{t("Placemaker.UI.ZoneMix.Empty", "Empty") + " " + (100 - fill) + "%"}</div>
                            </div>
                        ) : null}
                    </div>
                    <div className={z.line} data-testid="zonemix-line-fill" title={t("Placemaker.Polish.zoneFill.Tooltip", "")}>
                        <div className={z.lineLabel}>{t("Placemaker.Polish.zoneFill", "Fill")}</div>
                        <div className={z.cell}>
                            <Slider testId="zonemix-fill" value={fill} min={5} max={100} step={5} unit="%" onPreview={() => undefined} onCommit={(v) => set("zoneFill", v)} />
                        </div>
                        <div className={z.endSpacer} />
                    </div>
                    {used.map((x) => (
                        <div key={x.sl} className={z.zone} data-testid={"zonemix-zone-" + x.sl}>
                            <div className={z.line} data-testid={"zonemix-line-" + x.sl}>
                                <ZoneButton id={x.sl} kind={x.kind} asset={x.asset} />
                                <div className={z.cell}>
                                    {used.length > 1 ? (
                                        <Slider
                                            testId={"zonemix-share-" + x.sl}
                                            value={x.share}
                                            min={5}
                                            max={100 - 5 * (used.length - 1)}
                                            step={5}
                                            unit="%"
                                            onPreview={() => undefined}
                                            onCommit={(v) => reshare(x.sl, v, used.map((u) => u.sl))}
                                        />
                                    ) : (
                                        <div className={z.all}>{t("Placemaker.UI.ZoneMix.All", "all the zoned land")}</div>
                                    )}
                                </div>
                                {used.length > 1 ? (
                                    <button
                                        type="button"
                                        className={z.remove}
                                        data-testid={"zonemix-remove-" + x.sl}
                                        title={t("Placemaker.UI.ZoneMix.Remove", "Remove this zone")}
                                        onClick={(e) => {
                                            e.stopPropagation();
                                            removeZone(x.sl);
                                        }}
                                    >
                                        <Icon name="close" size={10} tint="dim" />
                                    </button>
                                ) : (
                                    <div className={z.endSpacer} />
                                )}
                            </div>
                            {menu === x.sl ? <Menu id={x.sl} kind={x.kind} /> : null}
                            <Sub label={t("Placemaker.UI.ZoneMix.Width", "Lot width")} tip={t("Placemaker.UI.ZoneMix.WidthTip", "How wide its lots are along the road, in zone cells (8 m): each lot picks a width in this range.")}>
                                <RangeSlider
                                    lo={x.w0}
                                    hi={x.w1}
                                    min={1}
                                    max={12}
                                    testId={"zonemix-width-" + x.sl}
                                    onCommit={(a, b) => {
                                        if (a !== x.w0) set("widthMin" + x.sl, a);
                                        if (b !== x.w1) set("widthMax" + x.sl, b);
                                    }}
                                />
                            </Sub>
                            <Sub label={t("Placemaker.UI.ZoneMix.Depth", "Lot depth")} tip={t("Placemaker.UI.ZoneMix.DepthTip", "How deep its lots go back from the road, in zone cells: each lot picks a depth in this range (6 at most).")}>
                                <RangeSlider
                                    lo={x.d0}
                                    hi={x.d1}
                                    min={1}
                                    max={6}
                                    testId={"zonemix-depth-" + x.sl}
                                    onCommit={(a, b) => {
                                        if (a !== x.d0) set("depthMin" + x.sl, a);
                                        if (b !== x.d1) set("depthMax" + x.sl, b);
                                    }}
                                />
                            </Sub>
                        </div>
                    ))}
                    {used.length < SLOTS.length ? (
                        <button
                            type="button"
                            className={z.add}
                            data-testid="zonemix-add"
                            onClick={(e) => {
                                e.stopPropagation();
                                addZone();
                            }}
                        >
                            <Icon name="plus" size={10} tint="accent" />
                            <div className={z.chipText}>{t("Placemaker.UI.ZoneMix.Add", "Mix in another zone")}</div>
                        </button>
                    ) : null}
                    {rows(val("zonePattern") === "clumps" ? ["zonePattern", "zoneGrain"] : ["zonePattern"])}
                    {rows(["roadRule"])}
                    {val("roadRule") === true ? (
                        <>
                            <div className={z.line} data-testid="zonemix-line-road">
                                <div className={z.lineLabel}>{t("Placemaker.Polish.roadZone", "Along big roads")}</div>
                                <ZoneButton id="road" kind={String(val("roadZone") ?? "")} asset="" />
                            </div>
                            {menu === "road" ? <Menu id="road" kind={String(val("roadZone") ?? "")} /> : null}
                            {rows(["roadMinWidth", "roadDepth"])}
                        </>
                    ) : null}
                </>
            ) : null}
        </div>
    );
};
