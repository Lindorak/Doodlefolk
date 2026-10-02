// The first-run tour of the Studio: a few cards that walk through what's where. It opens by itself the first
// time the Studio does (and from Settings → About whenever you like). Each step can switch page and point at a tab.

const TOUR = [
  { title: "Welcome to Doodlefolk! 👋", text: "Little stick figures now live on your desktop. They walk on your windows, make friends (and rivals), play, nap, get jobs and keep diaries. This is the Studio, where you can see and change everything. A quick look round?" },
  { page: "cast", tab: "cast", title: "Your cast", text: "Everyone living on your desktop. Click a card to see what makes them tick: personality, likes and dislikes, friends, mood, their diary, and their look. Draw someone new with the card at the end." },
  { page: "toys", tab: "toys", title: "Things", text: "Give them things: beds, couches, a campfire, bikes, a duck pond, a shop stall, a stage… They work out for themselves what each thing is for. You can also type anything (\"a giant red couch\") and it's drawn in." },
  { page: "pets", tab: "pets", title: "Pets", text: "Cats, dogs, parrots, rabbits and hamsters with real needs: feed them, scoop the litter box, take dogs for a walk on a leash (it runs from the collar to your cursor), and teach tricks." },
  { page: "paper", tab: "paper", title: "The Stick Times", text: "A weekly paper with everything that happened: new couples, rivalries, races, litters… and the sticker book next door fills up as you go." },
  { page: "settings", tab: "settings", title: "Settings", text: "Make it yours: how often it rains (or your real weather), town events, ageing, calm mode, quiet hours for when you're working, a battery saver, and much more." },
  { title: "On your desktop", text: "Drag a figure by a limb (let go while moving to throw it), move your cursor gently over one to pet it, and right-click anything for a quick menu. The tray icon (bottom right, by the clock) opens the Studio with a left-click, and a quick panel with a right-click." },
  { title: "That's it. Have fun! 🎉", text: "You can take this tour again from Settings → About." },
];

let tourAt = -1;

function startTour() {
  tourAt = 0;
  showTourStep();
}

function endTour() {
  tourAt = -1;
  document.querySelector(".tour")?.remove();
  for (const t of document.querySelectorAll(".tab.tour-here")) t.classList.remove("tour-here");
  send({ t: "tourDone" });
}

function showTourStep() {
  const step = TOUR[tourAt];
  if (!step) { endTour(); return; }
  if (step.page) go(step.page);
  for (const t of document.querySelectorAll(".tab")) t.classList.toggle("tour-here", t.dataset.page === step.tab);
  document.querySelector(".tour")?.remove();
  const last = tourAt === TOUR.length - 1;
  const card = h("div", { class: "tour", role: "dialog", "aria-label": "Doodlefolk tour" },
    h("div", { class: "tour-card" },
      h("div", { class: "tour-count" }, `${tourAt + 1} / ${TOUR.length}`),
      h("h2", null, step.title),
      h("p", null, step.text),
      h("div", { class: "row" },
        last ? null : h("button", { class: "btn small", onclick: endTour }, "Skip the tour"),
        h("span", { class: "spacer" }),
        tourAt > 0 ? h("button", { class: "btn small", onclick: () => { tourAt--; showTourStep(); } }, "Back") : null,
        h("button", { class: "btn small primary", onclick: () => { tourAt++; showTourStep(); } }, last ? "Done" : tourAt === 0 ? "Show me" : "Next"))));
  document.body.append(card);
  card.querySelector(".btn.primary").focus();
}

document.addEventListener("keydown", e => {
  if (tourAt < 0) return;
  if (e.key === "Escape") endTour();
  else if (e.key === "ArrowRight") { tourAt++; showTourStep(); }
  else if (e.key === "ArrowLeft" && tourAt > 0) { tourAt--; showTourStep(); }
});
