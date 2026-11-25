# Weld picking, cancel and polish (follow-up of #348)

Steps:
1. Cancel: no click-through while an order runs; a canceled pull stops the piece (no drift); every cancel path leaves a free piece and a new order works (test `weld-cancel`).
2. Picking: hover outline, click picks, ghost in the vanilla part brush art, right click / Esc back to picking, reasons for pieces that are not targets. Tab goes.
3. Pending (blueprint) weld shows its ghost.
4. Polish pass on the PC with several pieces; screenshots and a video.
5. Spec 71 in place, PR text, queue.
