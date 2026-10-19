// The Zoning types' Zoning tool (Core/Layers/Types/Zoning/PolishLookLayer; the Design page's Zoning section) as one editor
// instead of a row per setting. A switch: zone the empty land, or only edit the buildings there.
//   * Zoning: a bar of the land (each zone's share, the rest empty) whose dividers are dragged; labels that do not fit sit
//     above or below it on a line to their part. One line per zone: its colour and icon, its type from a short menu (or an
//     exact zone of the game), its lots' width and depth and the road widths it goes on (two-handle sliders), remove.
//     "Mix in another zone", then how the lots mix.
//   * Only edit existing: the zone types alone (each has its own Outgrow settings, below), nothing about placing.
// It writes the same settings the rows would (zoneA/B/C, zoneAssetA/B/C, shareA/B/C, …), so styles, undo, locks and
// Randomize keep working.
import React, { ReactNode, useLayoutEffect, useRef, useState } from "react";
import classNames from "classnames";
import { LayerType, SettingValue, StepView } from "../contract";
import { act, playSound } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { ControlEx, valueOf } from "../lib/schema";
import { Icon, Segmented } from "../components/common";
import { useAssetList, usePicker } from "../components/picker";
import z from "./zoneMix.module.scss";

const SLOTS = ["A", "B", "C"] as const;
type Slot = (typeof SLOTS)[number];
const MAX_ROAD = 48;

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

/** A layer whose zoning this editor draws. */
export const isZoneMix = (layer: LayerType) => {
    const ids = new Set(layer.controls.map((c) => c.id));
    return ids.has("zoneArea") && ids.has("shareA") && ids.has("zoneA");
};

/** A zone slot's id of an Outgrow setting (A: the plain id; B and C end in _B, _C). */
export const knobSlotOf = (id: string): number => (id.endsWith("_B") ? 1 : id.endsWith("_C") ? 2 : 0);

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
    const slots = SLOTS.map((sl, i) => ({
        sl,
        index: i,
        w0: num("widthMin" + sl, 2),
        w1: num("widthMax" + sl, 4),
        d0: num("depthMin" + sl, 2),
        d1: num("depthMax" + sl, 4),
        r0: num("roadMin" + sl, 0),
        r1: num("roadMax" + sl, MAX_ROAD),
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

/** The zones in use (for the Outgrow tabs): slot index, name, colour. */
export function useZoneTabs(step: StepView, layer: LayerType): { index: number; name: string; colour: string }[] {
    const { t } = useT();
    const zones = useAssetList("zone");
    const { ctl, used } = reader(step, layer);
    return used.map((x) => ({
        index: x.index,
        name: x.asset ? zones?.find((a) => a.id === x.asset)?.name || humanize(x.asset) : kindLabel(t, ctl, x.kind),
        colour: x.asset ? "#c9d3da" : ZONE_COLOR[x.kind] ?? "#c9d3da",
    }));
}

/** A range of whole numbers on one track with two handles (the nearer handle follows the mouse). */
const RangeSlider = ({ lo, hi, min, max, testId, text, onCommit }: { lo: number; hi: number; min: number; max: number; testId: string; text: (a: number, b: number) => string; onCommit: (lo: number, hi: number) => void }) => {
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
            <div className={z.rangeValue}>{text(a, b)}</div>
        </div>
    );
};

type Part = { key: string; sl: Slot | null; share: number; colour: string; name: string };

/**
 * The land as one bar: each zone's share and the empty rest. A divider between two parts is dragged to move land between
 * them (dragging into the empty part zones more, away from it zones less). A label that does not fit its part sits above
 * or below the bar on a line to it.
 */
const ShareBar = ({ parts, onCommit, t }: { parts: Part[]; onCommit: (shares: Record<string, number>) => void; t: TFunc }) => {
    const barRef = useRef<HTMLDivElement>(null);
    const [width, setWidth] = useState(0);
    const [drag, setDrag] = useState<number[] | null>(null);
    useLayoutEffect(() => {
        const w = barRef.current?.getBoundingClientRect().width ?? 0;
        if (Math.abs(w - width) > 1) setWidth(w);
    });
    const shares = drag ?? parts.map((p) => p.share);
    // the dividers: after every zone part (the last one too, so the empty part can be dragged out of a full bar)
    const zoneCount = parts.filter((p) => p.sl).length;
    const edges: number[] = [];
    let acc = 0;
    for (let i = 0; i < zoneCount; i++) {
        acc += shares[i];
        edges.push(acc);
    }
    const onDown = (e: React.MouseEvent, edge: number) => {
        if (e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        const r = barRef.current?.getBoundingClientRect();
        if (!r || r.width <= 0) return;
        const start = parts.map((p) => p.share);
        let cur = start.slice();
        const before = start.slice(0, edge).reduce((n, v) => n + v, 0);   // land left of the dragged part
        const right = edge + 1 < start.length ? start[edge + 1] : 0;       // the part to its right (a zone, or the empty rest)
        const apply = (clientX: number) => {
            const at = Math.round(Math.min(1, Math.max(0, (clientX - r.left) / r.width)) * 100);
            const lo = before + 1;                                     // the dragged part keeps 1 %
            const hi = before + start[edge] + right - (edge + 1 < zoneCount ? 1 : 0);
            const pos = Math.min(hi, Math.max(lo, at));
            cur = start.slice();
            cur[edge] = pos - before;
            if (edge + 1 < start.length) cur[edge + 1] = start[edge] + right - cur[edge];
            setDrag(cur);
        };
        apply(e.clientX);
        playSound("drag-slider");
        const move = (ev: MouseEvent) => apply(ev.clientX);
        const up = () => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            setDrag(null);
            const out: Record<string, number> = {};
            parts.forEach((p, i) => {
                if (p.sl && cur[i] !== start[i]) out[p.sl] = cur[i];
            });
            if (Object.keys(out).length) onCommit(out);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };
    // labels: inside when they fit, else above and below in turn
    const remPx = (() => {
        try {
            return parseFloat(getComputedStyle(document.documentElement).fontSize) || 1;
        } catch {
            return 1;
        }
    })();
    let left = 0;
    let flip = 0;
    const labels = parts.map((p, i) => {
        const text = p.name + " " + Math.round(shares[i]) + "%";
        const w = (shares[i] / 100) * width;
        const fits = w >= text.length * 6.4 * remPx + 10 * remPx;
        const centre = left + shares[i] / 2;
        left += shares[i];
        const place = fits || shares[i] <= 0 ? "in" : flip++ % 2 === 0 ? "above" : "below";
        return { text, centre, place, colour: p.colour, empty: !p.sl, key: p.key };
    });
    const above = labels.some((l) => l.place === "above");
    const below = labels.some((l) => l.place === "below");
    return (
        <div className={classNames(z.barBox, { [z.barBoxAbove]: above, [z.barBoxBelow]: below })} data-testid="zonemix-bar">
            <div className={z.bar} ref={barRef}>
                {parts.map((p, i) =>
                    shares[i] > 0 ? (
                        <div key={p.key} className={classNames(z.barPart, { [z.barEmpty]: !p.sl })} style={{ width: shares[i] + "%", backgroundColor: p.sl ? p.colour : undefined }} data-testid={"zonemix-part-" + p.key}>
                            {labels[i].place === "in" ? <div className={z.barText}>{labels[i].text}</div> : null}
                        </div>
                    ) : null,
                )}
                {edges.map((at, i) => (
                    <div key={"edge" + i} className={z.barEdge} style={{ left: Math.min(100, at) + "%" }} onMouseDown={(e) => onDown(e, i)} data-testid={"zonemix-edge-" + i} title={t("Placemaker.UI.ZoneMix.DragTip", "Drag to share the land")}>
                        <div className={z.barGrip} />
                    </div>
                ))}
            </div>
            {labels
                .filter((l) => l.place !== "in")
                .map((l) => (
                    <div key={"lbl" + l.key} className={classNames(z.callout, l.place === "above" ? z.calloutAbove : z.calloutBelow)} style={{ left: l.centre + "%" }}>
                        <div className={z.calloutLine} />
                        <div className={z.calloutText}>{l.text}</div>
                    </div>
                ))}
        </div>
    );
};

export const ZoneMix = ({ step, layer, rows }: { step: StepView; layer: LayerType; rows: (ids: string[]) => ReactNode }) => {
    const { t } = useT();
    const picker = usePicker();
    const zones = useAssetList("zone");
    const [menu, setMenu] = useState<string | null>(null);   // "A" | "B" | "C"
    const { ctl, val, slots, used } = reader(step, layer);
    const set = (id: string, v: SettingValue) => act.setValue(step.path, id, v);
    const placing = val("zoneArea") === true;

    /** The game's zone of a type (the city's EU or NA one when both exist). */
    const zoneOf = (kind: string, asset: string) => {
        if (asset) return zones?.find((a) => a.id === asset);
        const name = KIND_ZONE[kind];
        if (!name || !zones) return undefined;
        return zones.find((a) => a.id === name) ?? zones.find((a) => a.id.endsWith(" " + name));
    };
    const nameOf = (kind: string, asset: string) => (asset ? zoneOf(kind, asset)?.name || humanize(asset) : kindLabel(t, ctl, kind));
    const colourOf = (kind: string, asset: string) => (asset ? "#c9d3da" : ZONE_COLOR[kind] ?? "#c9d3da");

    // the bar: each zone's share of the land, the rest empty (shares above 100 together: shared out, nothing empty)
    const sum = used.reduce((n, x) => n + x.share, 0);
    const scale = sum > 100 ? 100 / sum : 1;
    const parts: Part[] = used.map((x) => ({ key: x.sl, sl: x.sl, share: Math.round(x.share * scale), colour: colourOf(x.kind, x.asset), name: nameOf(x.kind, x.asset) }));
    const zoned = parts.reduce((n, p) => n + p.share, 0);
    parts.push({ key: "empty", sl: null, share: Math.max(0, 100 - zoned), colour: "", name: t("Placemaker.UI.ZoneMix.Empty", "Empty") });

    const commitShares = (out: Record<string, number>) => {
        // a bar that was over 100 is written out as it is shown
        if (scale < 1) for (const p of parts) if (p.sl && !(p.sl in out)) out[p.sl] = p.share;
        for (const [sl, v] of Object.entries(out)) set("share" + sl, Math.max(1, Math.round(v)));
    };

    const addZone = () => {
        const free = slots.find((x) => x.share <= 0);
        if (!free) return;
        const usedKinds = new Set(used.map((x) => x.kind));
        if (usedKinds.has(free.kind)) {
            const kind = KIND_GROUPS.flatMap((g) => g.kinds).find((k) => !usedKinds.has(k));
            if (kind) set("zone" + free.sl, kind);
        }
        // its land: from the empty part when there is some, else a slice of the biggest zone
        const empty = Math.max(0, 100 - zoned);
        if (!placing || empty >= 10) set("share" + free.sl, placing ? Math.min(25, empty) : 25);
        else {
            const big = parts.filter((p) => p.sl).sort((p, q) => q.share - p.share)[0];
            const give = Math.max(1, Math.round(big.share / 3));
            set("share" + big.sl, big.share - give);
            set("share" + free.sl, give);
        }
        setMenu(free.sl);
    };

    const removeZone = (sl: Slot) => {
        if (used.length <= 1) return;
        set("share" + sl, 0);
        if (menu === sl) setMenu(null);
    };

    const pickKind = (sl: string, kind: string) => {
        set("zone" + sl, kind);
        if (slots.find((x) => x.sl === sl)?.asset) set("zoneAsset" + sl, "");
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
        </div>
    );

    const roadText = (a: number, b: number) =>
        a <= 0 && b >= MAX_ROAD ? t("Placemaker.UI.ZoneMix.AnyRoad", "any") : a <= 0 ? "≤ " + b + " m" : b >= MAX_ROAD ? "≥ " + a + " m" : a + "–" + b + " m";

    return (
        <div className={z.mix} data-testid="zonemix">
            <Segmented
                testId="zonemix-mode"
                stretch
                value={placing ? "zone" : "edit"}
                onChange={(v) => set("zoneArea", v === "zone")}
                options={[
                    { value: "zone", label: t("Placemaker.UI.ZoneMix.ModeZone", "Zone empty land"), description: t("Placemaker.UI.ZoneMix.ModeZoneTip", "The empty zone cells of the area get these zones, as lots that start at the road; the buildings there outgrow their lots too.") },
                    { value: "edit", label: t("Placemaker.UI.ZoneMix.ModeEdit", "Only edit existing"), description: t("Placemaker.UI.ZoneMix.ModeEditTip", "Nothing is zoned: the buildings in the area outgrow their lots, each zone type by its own settings below (a type not listed: the first one's).") },
                ]}
            />
            {placing ? <ShareBar parts={parts} onCommit={commitShares} t={t} /> : <div className={z.modeGap} />}
            {used.map((x) => (
                <div key={x.sl} className={z.zone} data-testid={"zonemix-zone-" + x.sl}>
                    <div className={z.line} data-testid={"zonemix-line-" + x.sl}>
                        <ZoneButton id={x.sl} kind={x.kind} asset={x.asset} />
                        {placing ? (
                            <div className={z.ranges}>
                                <div className={z.range} title={t("Placemaker.UI.ZoneMix.WidthTip", "How wide its lots are along the road, in zone cells (8 m): each lot picks a width in this range.")}>
                                    <div className={z.rangeLabel}>{t("Placemaker.UI.ZoneMix.W", "Width")}</div>
                                    <RangeSlider
                                        lo={x.w0}
                                        hi={x.w1}
                                        min={1}
                                        max={12}
                                        testId={"zonemix-width-" + x.sl}
                                        text={(a, b) => (a === b ? String(a) : a + "–" + b)}
                                        onCommit={(a, b) => {
                                            if (a !== x.w0) set("widthMin" + x.sl, a);
                                            if (b !== x.w1) set("widthMax" + x.sl, b);
                                        }}
                                    />
                                </div>
                                <div className={z.range} title={t("Placemaker.UI.ZoneMix.DepthTip", "How deep its lots go back from the road, in zone cells: each lot picks a depth in this range (6 at most).")}>
                                    <div className={z.rangeLabel}>{t("Placemaker.UI.ZoneMix.D", "Depth")}</div>
                                    <RangeSlider
                                        lo={x.d0}
                                        hi={x.d1}
                                        min={1}
                                        max={6}
                                        testId={"zonemix-depth-" + x.sl}
                                        text={(a, b) => (a === b ? String(a) : a + "–" + b)}
                                        onCommit={(a, b) => {
                                            if (a !== x.d0) set("depthMin" + x.sl, a);
                                            if (b !== x.d1) set("depthMax" + x.sl, b);
                                        }}
                                    />
                                </div>
                                <div className={z.range} title={t("Placemaker.UI.ZoneMix.RoadTip", "The roads it goes on, by their width (a two-lane road is 16 m, four lanes 24 m, six lanes 32 m): its lots are only placed along roads this wide.")}>
                                    <div className={z.rangeLabel}>{t("Placemaker.UI.ZoneMix.Roads", "Roads")}</div>
                                    <RangeSlider
                                        lo={x.r0}
                                        hi={x.r1}
                                        min={0}
                                        max={MAX_ROAD}
                                        testId={"zonemix-roads-" + x.sl}
                                        text={roadText}
                                        onCommit={(a, b) => {
                                            if (a !== x.r0) set("roadMin" + x.sl, a);
                                            if (b !== x.r1) set("roadMax" + x.sl, b);
                                        }}
                                    />
                                </div>
                            </div>
                        ) : (
                            <div className={z.flex} />
                        )}
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
                    <div className={z.chipText}>{placing ? t("Placemaker.UI.ZoneMix.Add", "Mix in another zone") : t("Placemaker.UI.ZoneMix.AddType", "Another zone type")}</div>
                </button>
            ) : null}
            {placing ? rows(val("zonePattern") === "clumps" ? ["zonePattern", "zoneGrain"] : ["zonePattern"]) : null}
        </div>
    );
};
