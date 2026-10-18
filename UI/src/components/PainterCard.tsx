// (PROTOCOL 25) The neighbourhood-polish painter ("Polish gardens" in the Make row): what it does, how many lots the area
// holds and how many are polished, and a warning while the polish itself is switched off in the settings. Polish and
// Unpolish are the footer's buttons (Panel.tsx Footer).
import React from "react";
import { ToolState } from "../contract";
import { useT } from "../lib/l10n";
import { Icon } from "./common";
import s from "./painter.module.scss";

export const PainterCard = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const p = state.painter;
    if (!p) return null;
    const hasArea = state.area.closed && state.area.areaM2 > 0;
    return (
        <div className={s.card} data-testid="painter">
            <div className={s.title}>
                <Icon name="sparkle" size={13} tint="light" />
                <div className={s.titleText}>{t("Placemaker.UI.Painter.Title", "Polish gardens")}</div>
            </div>
            <div className={s.line}>
                {t("Placemaker.UI.Painter.How", "Pick houses with Points, Paint bucket or Paint area, then press Polish or Unpolish. Only polished gardens are joined up; Unpolish puts a house back as the game made it.")}
            </div>
            {hasArea ? (
                <div className={s.count} data-testid="painter-count" data-lots={p.lots} data-polished={p.polished}>
                    <div className={s.swatch} />
                    <div>{t("Placemaker.UI.Painter.Count", "{0} houses in the area · {1} polished", [p.lots, p.polished])}</div>
                </div>
            ) : (
                <div className={s.count}>
                    <div className={s.swatch} />
                    <div>{t("Placemaker.UI.Painter.Legend", "Green: polished houses")}</div>
                </div>
            )}
            {!p.featureOn ? (
                <div className={s.off} data-testid="painter-off">
                    <Icon name="warning" size={12} tint="warn" />
                    <div className={s.offText}>
                        {t("Placemaker.UI.Painter.Off", "Neighbourhood polish is off in the settings: the houses are marked, their gardens change once it is on.")}
                    </div>
                </div>
            ) : null}
        </div>
    );
};
