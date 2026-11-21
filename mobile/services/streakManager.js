/**
 * streakManager.js — Local Eco-Streak & Cosmetic Skin Unlock Manager
 *
 * Tracks the player's daily verification streak in memory (swap to
 * AsyncStorage for persistence if time allows). Determines which
 * cosmetic skins unlock at which milestones.
 * Now also tracks cumulative CO₂ savings from the backend.
 */

// ─── Cosmetic Skin Catalog ────────────────────────────────────
export const SKIN_CATALOG = [
  {
    id: "seedling",
    name: "Seedling Sprout",
    description: "Your first step toward a greener world!",
    requiredStreak: 1,
    rarity: "Common",
    emoji: "🌱",
  },
  {
    id: "leaf_warrior",
    name: "Leaf Warrior",
    description: "Three days of recycling? You're on fire!",
    requiredStreak: 3,
    rarity: "Uncommon",
    emoji: "🍃",
  },
  {
    id: "ocean_guardian",
    name: "Ocean Guardian",
    description: "A full week! The oceans thank you.",
    requiredStreak: 7,
    rarity: "Rare",
    emoji: "🌊",
  },
  {
    id: "earth_champion",
    name: "Earth Champion",
    description: "Two weeks of dedication. Legendary status.",
    requiredStreak: 14,
    rarity: "Epic",
    emoji: "🌍",
  },
  {
    id: "recycling_god",
    name: "Recycling Deity",
    description: "30-day streak. You ARE recycling.",
    requiredStreak: 30,
    rarity: "Legendary",
    emoji: "♻️",
  },
];

// ─── State (in-memory for hackathon; replace with AsyncStorage) ─
let currentStreak = 0;
let unlockedSkinIds = new Set();
let lastVerifiedDate = null;
let totalCo2Grams = 0;
let totalItemsRecycled = 0;

/**
 * Call this when the backend returns `verified: true`.
 * Increments the streak (or resets if a day was missed) and
 * returns any *newly* unlocked skins.
 *
 * @param {number} co2Grams - CO₂ saved from this scan (from backend)
 * @returns {{ streak: number, newUnlocks: Array, totalCo2Grams: number }}
 */
export function recordVerifiedStreak(co2Grams = 0) {
  const today = new Date().toDateString();

  if (lastVerifiedDate === today) {
    // Already verified today — still count the CO₂ but no streak bump
    totalCo2Grams += co2Grams;
    totalItemsRecycled += 1;
    return { streak: currentStreak, newUnlocks: [], totalCo2Grams, totalItemsRecycled };
  }

  const yesterday = new Date(Date.now() - 86400000).toDateString();
  if (lastVerifiedDate === yesterday) {
    currentStreak += 1;
  } else {
    currentStreak = 1;
  }
  lastVerifiedDate = today;

  totalCo2Grams += co2Grams;
  totalItemsRecycled += 1;

  // Check for newly unlocked skins at this streak level
  const newUnlocks = SKIN_CATALOG.filter(
    (skin) =>
      skin.requiredStreak <= currentStreak && !unlockedSkinIds.has(skin.id)
  );
  newUnlocks.forEach((skin) => unlockedSkinIds.add(skin.id));

  return { streak: currentStreak, newUnlocks, totalCo2Grams, totalItemsRecycled };
}

/**
 * Returns the current state for display purposes.
 */
export function getStreakState() {
  return {
    currentStreak,
    totalCo2Grams,
    totalCo2Kg: (totalCo2Grams / 1000).toFixed(2),
    totalItemsRecycled,
    unlockedSkins: SKIN_CATALOG.filter((s) => unlockedSkinIds.has(s.id)),
    nextSkin: SKIN_CATALOG.find((s) => !unlockedSkinIds.has(s.id)) || null,
    allSkins: SKIN_CATALOG,
  };
}
