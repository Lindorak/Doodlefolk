# How Doodlefolk minds work (and where they're going)

Everything here runs on your PC, offline, with no language models or cloud services: the "smarts" come from the
same kinds of systems the most believable game characters use (The Sims, F.E.A.R., RimWorld, Dwarf Fortress,
Crusader Kings), chosen from game-AI research and talks (references at the end). Runs are deterministic for a given
seed, so the simulation tests (`--simtest`) can replay any town exactly.

## The layers

Higher layers never move a body. They add scored options to the layer below; needs and emergencies can always
interrupt, and an interrupted plan is suspended and resumed, not lost. (In belief–desire–intention terms: dreams are
desires, projects are intentions, the everyday chooser carries them out.)

| Layer | Time scale | What it is | Seen as |
|---|---|---|---|
| **Dreams** (life goals) | days–weeks | One per figure, chosen from personality, likes, memories and what the town lacks; milestones; given up when achieved, impossible or stale (with a margin so they don't flip-flop) | "Dream:" on their card, a morning line ("Today I'm practising juggling!"), diary entries, a long-lasting pride mood at each milestone |
| **Projects** (plans) | minutes–hours | Hierarchical task network (HTN) recipes: "throw a party" → tidy, get food, invite friends, decorate, host; several methods per task chosen by personality and what's available; replanned when a step fails | A thought-bubble icon, step lines ("Off to invite Bea!"), a sigh and a new plan when something's in the way |
| **Choosing** (utility) | seconds | Every option scored by response curves over needs, mood, personality, likes, distance, learned habits, novelty and relationships; ranked buckets (emergency, obligation, project, needs, idle) so a plan never fights hunger on one flat scale; weighted random among the best; commitment and cooldowns against dithering | What they do |
| **Things** | — | Objects advertise what they offer (sit, eat, hide, play, *move me*), with slots that can be reserved so two people don't head for the same chair | — |

Alongside:

- **Memory**: a fixed ring of episodes per figure (what, who, where, how it felt, how important), recalled by
  recency × importance × relevance; folded nightly into lasting opinions, favourite places and grudges that fade
  (faster for forgiving people). Source of diary lines and remarks ("This is where I won the race!").
- **Gossip and reputation**: in a chat, figures pass on what they believe about someone else, with confidence shrinking
  at each telling (and now and then a little exaggeration); capped so a town doesn't pile on one person.
- **Emotion**: events appraised against goals, standards and tastes (joy, distress, pride, shame, gratitude, anger,
  hope, fear) as timed "thoughts" that add up to mood; mood shifts choices (happier → more social and playful;
  agitated → less predictable). Acting against one's nature builds stress, which eventually breaks out (with relief
  afterwards so it doesn't spiral).
- **Contagion and copying**: moods spread to nearby friends (pulled back towards each person's own baseline); seeing
  someone you admire enjoy something makes you more likely to try it (fads), within what fits your personality.
- **Social practices**: parties, visits, festivals and races give roles, obligations and expected behaviour; breaking
  them in front of others brings shame and disapproval.

## Rules of thumb (from the research)

- Intent must be visible: a plan the player can't see doesn't exist. Every new dream, step, replan and outcome gets a
  line, an icon or a diary entry.
- Not too perfect: some randomness, forgetting, procrastination for low-energy people, the odd abandoned plan.
- Change fits character: habits and fads are bounded by personality; a shy figure doesn't become a party animal overnight.
- Variety without randomness: gate dreams on personality, and don't let half the town chase the same one.
- Caps and decay on every feedback loop (moods, gossip, grudges, stress).
- No allocations in hot paths, thinking staggered across frames, at most one plan built per frame; per-figure random
  streams so one figure's choices don't shift another's.
- Explain every decision: the debug view shows the top options and their scores, the plan, the moods.

## References

- Kevin Dill, *Dual-Utility Reasoning* (Game AI Pro 2, ch. 3) — ranks plus weights; weighted random among the best.
- Mike Lewis, *Choosing Effective Utility-Based Considerations* (Game AI Pro 3, ch. 13); Dave Mark & Mike Lewis,
  *Building a Better Centaur* (GDC 2015) — response curves, multiplying considerations, compensation, anti-repeat curves.
- Richard Evans, *Modeling Individual Personalities in The Sims 3* (GDC 2010) — traits as extra motives, hierarchical
  choice, mood-dependent randomness, story progression, "build visualisation tools".
- EA, *The Sims 4: Whims, Aspirations and Goals*; The Sims Wiki, *Emotion* — short wants under long aspirations; moodlets.
- Jeff Orkin, *Three States and a Plan: The AI of F.E.A.R.* (GDC 2006); *Combat Dialogue in F.E.A.R.* (Game AI Pro 2).
- Troy Humphreys, *Exploring HTN Planners through Example* (Game AI Pro 1, ch. 12); Tim Verweij, *HTN Planning in Decima*.
- Paradox, *CK3 Dev Diary #31: A Stressful Situation*; RimWorld wiki (Thoughts, Mood, Mental breaks, Inspirations,
  Storytellers); Dwarf Fortress wiki (Personality facets, values, needs, goals).
- Rao & Georgeff, *BDI Agents: From Theory to Practice* (1995); Black & White's creature AI.
- Park et al., *Generative Agents* (2023) — memory retrieval by recency, importance and relevance; reflection
  (used here without any language model).
- McCoy et al., *Prom Week: Social Physics as Gameplay* (FDG 2011); Evans & Short, *The AI Architecture of Versu*;
  Ryan et al., *Toward Characters Who Observe, Tell, Misremember, and Lie* (Talk of the Town).
- Popescu, Broekens & van Someren, *GAMYGDALA: An Emotion Engine for Games*; Ortony, Clore & Collins, *The Cognitive
  Structure of Emotions*; Mehrabian's pleasure–arousal–dominance model.
- Bosse et al., *Agent-Based Modelling of Emotion Contagion in Groups* (2015).
- A. Bryan Loyall, *Believable Agents* (CMU, 1997); Michael Mateas, *An Oz-Centric Review of Interactive Drama and
  Believable Agents*.
