// Section in the vanilla selected-info panel for anything built by Placemaker:
//   "Part of <name>" — Edit / Rename / Remove.
// Registered for the C# InfoSectionBase `Placemaker.Game.Groups.PlacemakerGroupSection` (the key is the
// full .NET class name: InfoSectionBase writes GetType().FullName as __Type — ParkingLotTool learned
// that the hard way). Props = what Game/Groups/PlacemakerGroupSection.OnWriteProperties writes:
// groupId, name, recipeNameKey, stats[], upkeep, memberCount, missingAssets, modified, removing.
// Unknown/missing fields degrade gracefully.
import React, { useState } from "react";
import { act } from "../lib/bindings";
import { humanize, useT } from "../lib/l10n";
import { formatValue, money } from "../lib/format";
import { ErrorBoundary } from "../lib/errorBoundary";
import { VC } from "../lib/vanilla";
import { useGroupActions, RenameField, UngroupButton } from "../stages/ManageStage";
import { Btn, Icon } from "./common";
import s from "./infoSection.module.scss";

export const INFO_SECTION_TYPE = "Placemaker.Game.Groups.PlacemakerGroupSection";

type Props = {
    groupId?: number;
    id?: number;
    group?: number | string;
    name?: string;
    customName?: string;
    recipeName?: string | { key: string; args?: (string | number)[] };
    recipeNameKey?: string;
    canEdit?: boolean;
    stats?: { key: string; value: number; unit: string }[];
    upkeep?: number;
    memberCount?: number;
    missingAssets?: boolean;
    modified?: boolean;
    removing?: boolean;
    ungrouped?: boolean;
};

const Inner = (p: Props) => {
    const { t, tl } = useT();
    const [renaming, setRenaming] = useState(false);
    const id = typeof p.groupId === "number" ? p.groupId : typeof p.id === "number" ? p.id : typeof p.group === "number" ? p.group : -1;
    const recipeKey = p.recipeNameKey || (typeof p.recipeName === "string" ? p.recipeName : "");
    const recipe = recipeKey ? t(recipeKey, "") : tl(typeof p.recipeName === "object" ? p.recipeName : undefined);
    const busy = !!p.removing;
    const stats = Array.isArray(p.stats) ? p.stats.filter((x) => x && x.unit === "count").slice(0, 3) : [];
    const name = p.customName || p.name || recipe || t("Placemaker.Name", "Placemaker");
    const { askRemove, askUngroup, dialog } = useGroupActions(id, name);
    const content = (
        <div className={s.section} data-testid="info-section">
            <div className={s.head}>
                <Icon name="logo" size={18} color="#8fd1ff" />
                {renaming ? (
                    <RenameField id={id} name={p.customName ?? p.name ?? ""} onDone={() => setRenaming(false)} />
                ) : (
                    <div className={s.title}>
                        <span className={s.partOf}>{t("Placemaker.UI.Info.PartOf", "Part of")}</span> {name}
                    </div>
                )}
            </div>
            {p.customName || p.name ? <div className={s.kind}>{recipe}</div> : null}
            {stats.length || typeof p.upkeep === "number" ? (
                <div className={s.facts}>
                    {stats.map((x) => formatValue(x.value, "count") + " " + t(x.key, humanize(x.key).toLowerCase())).join(" · ")}
                    {typeof p.upkeep === "number" ? (stats.length ? " · " : "") + money(p.upkeep) + t("Placemaker.UI.PerMonth", "/mo") : ""}
                </div>
            ) : null}
            {p.missingAssets ? (
                <div className={s.note} data-testid="info-missing">
                    <Icon name="warning" size={14} color="#ffb547" />
                    <div className={s.noteText}>{t("Placemaker.UI.Info.Missing", "Some assets it used are missing (a mod or asset pack was removed). Edit to replace them.")}</div>
                </div>
            ) : null}
            {p.modified ? (
                <div className={s.note}>
                    <Icon name="info" size={14} color="#8fd1ff" />
                    <div className={s.noteText}>{t("Placemaker.UI.Info.Modified", "Changed with other tools since it was built. Editing rebuilds it from its design.")}</div>
                </div>
            ) : null}
            {p.ungrouped ? (
                <div className={s.note} data-testid="info-ungrouped">
                    <Icon name="info" size={14} color="#8fd1ff" />
                    <div className={s.noteText}>{t("Placemaker.UI.Info.Ungrouped", "Ungrouped: you click and bulldoze its pieces one at a time. Remove takes all of it.")}</div>
                </div>
            ) : null}
            {busy ? <div className={s.facts}>{t("Placemaker.UI.Info.Removing", "Removing…")}</div> : null}
            {!renaming && !busy ? (
                <div className={s.actions}>
                    <Btn variant="solid" icon="edit" iconSize={14} label={t("Placemaker.UI.Manage.Edit", "Edit")} disabled={p.canEdit === false || id < 0} onClick={() => act.editGroup(id)} testId="info-edit" />
                    <Btn variant="solid" label={t("Placemaker.UI.Manage.Rename", "Rename")} disabled={id < 0} onClick={() => setRenaming(true)} testId="info-rename" />
                    <UngroupButton id={id} ungrouped={!!p.ungrouped} askUngroup={askUngroup} testId="info-ungroup" />
                    <div className={s.flex} />
                    <Btn variant="danger" icon="trash" iconSize={14} label={t("Placemaker.UI.Manage.Remove", "Remove")} disabled={id < 0} onClick={askRemove} testId="info-remove" />
                </div>
            ) : null}
            {dialog}
        </div>
    );
    return VC.InfoSection ? <VC.InfoSection disableFocus={true}>{content}</VC.InfoSection> : content;
};

export const PlacemakerInfoSection = (p: Props) => (
    <ErrorBoundary name="info-section" fallback={null}>
        <Inner {...p} />
    </ErrorBoundary>
);
