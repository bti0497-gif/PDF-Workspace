using System;
using System.Collections.Generic;
using System.Windows;

namespace Workspace.Shell.Presentation;

// A logical gap may be reached from either card across a wrapped row.
// Keep its visual anchor so "after page 2" stays beside page 2, not above page 3.
internal readonly record struct ThumbnailInsertionTarget(int Slot, int CardIndex, bool After)
{
    internal static ThumbnailInsertionTarget Invalid => new(-1,-1,false);
}
internal static class ThumbnailReorder
{
    internal static ThumbnailInsertionTarget Resolve(IReadOnlyList<Rect> cards,Size contentSize,Point point)
    {
        if(cards.Count==0||!double.IsFinite(point.X)||!double.IsFinite(point.Y)||
           point.X<0||point.Y<0||point.X>contentSize.Width||point.Y>contentSize.Height)
            return ThumbnailInsertionTarget.Invalid;
        var nearest=-1;var distance=double.PositiveInfinity;
        for(var i=0;i<cards.Count;i++)
        {
            var card=cards[i];if(card.IsEmpty||card.Width<=0||card.Height<=0)continue;
            var dx=Math.Max(0,Math.Max(card.Left-point.X,point.X-card.Right));
            var dy=Math.Max(0,Math.Max(card.Top-point.Y,point.Y-card.Bottom));
            var squared=dx*dx+dy*dy;
            // Ties use the preceding card, making the exact middle of a gutter stable.
            if(squared<distance){distance=squared;nearest=i;}
        }
        if(nearest<0)return ThumbnailInsertionTarget.Invalid;
        var target=cards[nearest];
        var outsideX=Math.Max(0,Math.Max(target.Left-point.X,point.X-target.Right));
        var outsideY=Math.Max(0,Math.Max(target.Top-point.Y,point.Y-target.Bottom));
        // At a diagonal row gutter, use the dominant approach direction; the marker stays vertical.
        var after=outsideY>outsideX?(point.Y>target.Bottom):
            point.X<target.Left?false:point.X>target.Right?true:
            point.X>=target.Left+target.Width/2;
        return new(nearest+(after?1:0),nearest,after);
    }

    internal static int FindSlot(IReadOnlyList<Rect> cards,Size contentSize,Point point)
        =>Resolve(cards,contentSize,point).Slot;

    // Slot-only previews use the previous card's right edge; pointer previews preserve the hovered card.
    internal static Rect Marker(IReadOnlyList<Rect> cards,int slot)
        =>slot<0||slot>cards.Count?Rect.Empty:
            Marker(cards,new ThumbnailInsertionTarget(slot,slot==0?0:slot-1,slot!=0));

    internal static Rect Marker(IReadOnlyList<Rect> cards,ThumbnailInsertionTarget target)
    {
        if(target.Slot<0||target.Slot>cards.Count||target.CardIndex<0||target.CardIndex>=cards.Count||
           target.Slot!=target.CardIndex+(target.After?1:0))return Rect.Empty;
        var card=cards[target.CardIndex];if(card.IsEmpty||card.Width<=0||card.Height<=0)return Rect.Empty;
        const double thickness=3;
        var x=target.After?card.Right+6:card.Left-6;
        return new Rect(Math.Max(0,x-thickness/2),card.Top,thickness,card.Height);
    }
}
