/**
 * SkinUnlockOverlay.js — Cosmetic Skin Unlock Celebration
 *
 * A full-screen animated overlay that plays when the player unlocks
 * one or more new cosmetic skins from reaching a streak milestone.
 * Auto-dismisses after a few seconds or on tap.
 */
import React, { useState, useEffect, useRef } from "react";
import {
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
  Animated,
  Dimensions,
} from "react-native";

const { width: SCREEN_WIDTH } = Dimensions.get("window");

const RARITY_COLORS = {
  Common: "#95A5A6",
  Uncommon: "#2ECC71",
  Rare: "#3498DB",
  Epic: "#9B59B6",
  Legendary: "#F39C12",
};

export default function SkinUnlockOverlay({ skins }) {
  const [visible, setVisible] = useState(true);
  const [currentIndex, setCurrentIndex] = useState(0);
  const fadeAnim = useRef(new Animated.Value(0)).current;
  const scaleAnim = useRef(new Animated.Value(0.3)).current;
  const glowAnim = useRef(new Animated.Value(0)).current;

  const currentSkin = skins[currentIndex];
  const rarityColor = RARITY_COLORS[currentSkin?.rarity] || "#fff";

  useEffect(() => {
    // Entrance animation
    Animated.parallel([
      Animated.timing(fadeAnim, {
        toValue: 1,
        duration: 400,
        useNativeDriver: true,
      }),
      Animated.spring(scaleAnim, {
        toValue: 1,
        friction: 4,
        tension: 80,
        useNativeDriver: true,
      }),
    ]).start();

    // Pulsing glow
    Animated.loop(
      Animated.sequence([
        Animated.timing(glowAnim, {
          toValue: 1,
          duration: 800,
          useNativeDriver: true,
        }),
        Animated.timing(glowAnim, {
          toValue: 0,
          duration: 800,
          useNativeDriver: true,
        }),
      ])
    ).start();

    // Auto-advance or dismiss after 4 seconds
    const timer = setTimeout(() => {
      if (currentIndex < skins.length - 1) {
        setCurrentIndex((i) => i + 1);
      } else {
        setVisible(false);
      }
    }, 4000);

    return () => clearTimeout(timer);
  }, [currentIndex]);

  if (!visible || !currentSkin) return null;

  return (
    <TouchableOpacity
      style={styles.overlay}
      activeOpacity={1}
      onPress={() => {
        if (currentIndex < skins.length - 1) {
          setCurrentIndex((i) => i + 1);
        } else {
          setVisible(false);
        }
      }}
    >
      <Animated.View
        style={[
          styles.card,
          {
            opacity: fadeAnim,
            transform: [{ scale: scaleAnim }],
            borderColor: rarityColor,
            shadowColor: rarityColor,
          },
        ]}
      >
        <Text style={styles.unlockLabel}>🎊 SKIN UNLOCKED 🎊</Text>

        <Animated.Text
          style={[
            styles.skinEmoji,
            {
              opacity: glowAnim.interpolate({
                inputRange: [0, 1],
                outputRange: [0.7, 1],
              }),
              transform: [
                {
                  scale: glowAnim.interpolate({
                    inputRange: [0, 1],
                    outputRange: [1, 1.15],
                  }),
                },
              ],
            },
          ]}
        >
          {currentSkin.emoji}
        </Animated.Text>

        <Text style={styles.skinName}>{currentSkin.name}</Text>
        <Text style={[styles.skinRarity, { color: rarityColor }]}>
          {currentSkin.rarity}
        </Text>
        <Text style={styles.skinDescription}>{currentSkin.description}</Text>

        <Text style={styles.tapHint}>Tap to continue</Text>
      </Animated.View>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  overlay: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: "rgba(0, 0, 0, 0.85)",
    justifyContent: "center",
    alignItems: "center",
    zIndex: 999,
  },
  card: {
    backgroundColor: "#1a1a2e",
    borderRadius: 24,
    padding: 36,
    alignItems: "center",
    width: SCREEN_WIDTH * 0.8,
    borderWidth: 2,
    shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.8,
    shadowRadius: 20,
    elevation: 15,
  },
  unlockLabel: {
    color: "#F1C40F",
    fontSize: 18,
    fontWeight: "bold",
    letterSpacing: 2,
    marginBottom: 20,
  },
  skinEmoji: {
    fontSize: 80,
    marginBottom: 16,
  },
  skinName: {
    color: "#fff",
    fontSize: 24,
    fontWeight: "bold",
    marginBottom: 4,
    textAlign: "center",
  },
  skinRarity: {
    fontSize: 14,
    fontWeight: "bold",
    textTransform: "uppercase",
    letterSpacing: 1.5,
    marginBottom: 12,
  },
  skinDescription: {
    color: "#aaa",
    fontSize: 14,
    textAlign: "center",
    lineHeight: 20,
    marginBottom: 20,
  },
  tapHint: {
    color: "#555",
    fontSize: 12,
  },
});
