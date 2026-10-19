// The Zoning types' "Zone this area" group (Core/Layers/Types/Zoning/PolishLookLayer) as one editor instead of a row per
// setting: a bar of the mix, one line per zone (its colour and icon, its type from a short menu or an exact zone of the
// game, its share), "Add a zone", then the pattern and the road rule. It writes the same settings the rows would
// (zoneA/B/C, zoneAssetA/B/C, shareA/B/C, …), so styles, undo, locks and Randomize keep working.
import React, { ReactNode, useState } from "react";
import classNames from "classnames";
import { LayerType, SettingValue, StepView } from "../contract";
import { act } from "../lib/bindings";
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
    const slots = SLOTS.map((sl) => ({
        sl,
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
    return used.map((x) => (x.asset ? assetName("zone", x.asset) : kindLabel(t, ctl, x.kind)) + " " + Math.round((x.share / total) * 100) + "%").join(" · ");
}

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

    return (
        <div className={z.mix} data-testid="zonemix">
            {rows(["zoneArea"])}
            {on ? (
                <>
                    <div className={z.bar} data-testid="zonemix-bar">
                        {used.map((x) => (
                            <div key={x.sl} className={z.barPart} style={{ width: (x.share / total) * 100 + "%", backgroundColor: colourOf(x.kind, x.asset) }} />
                        ))}
                    </div>
                    {used.map((x) => (
                        <React.Fragment key={x.sl}>
                            <div className={z.line} data-testid={"zonemix-line-" + x.sl}>
                                <ZoneButton id={x.sl} kind={x.kind} asset={x.asset} />
                                {used.length > 1 ? (
                                    <>
                                        <div className={z.share}>
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
                                        </div>
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
                                    </>
                                ) : (
                                    <div className={z.all}>{t("Placemaker.UI.ZoneMix.All", "the whole area")}</div>
                                )}
                            </div>
                            {menu === x.sl ? <Menu id={x.sl} kind={x.kind} /> : null}
                        </React.Fragment>
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
                    {rows(val("zonePattern") === "speckled" ? ["zonePattern"] : ["zonePattern", "zoneGrain"])}
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
