/**
 * DetectionOverlay.js — The captured photo with the detected item's outline.
 *
 * The server returns `contour` as [[x, y], ...] in 0..1 of the upright photo
 * and `image_size` as [w, h]. We show the photo at that aspect ratio and draw
 * the outline in an SVG whose viewBox is the unit square, so points map 1:1
 * whatever size the photo is drawn at.
 */
import React from "react";
import { View, Image, Text, StyleSheet } from "react-native";
import Svg, { Polygon } from "react-native-svg";

const MAX_WIDTH = 300;
const MAX_HEIGHT = 400;

export default function DetectionOverlay({ photoUri, result }) {
  if (!photoUri) return null;

  const [w, h] = result?.image_size || [4, 3];
  const scale = Math.min(MAX_WIDTH / w, MAX_HEIGHT / h);
  const size = { width: Math.round(w * scale), height: Math.round(h * scale) };

  const contour = result?.contour;
  const colour = result?.verified ? "#2ECC71" : "#E74C3C";
  const points = contour?.map(([x, y]) => `${x},${y}`).join(" ");
  const label = result?.detected_item || result?.detected_class;

  return (
    <View style={[styles.frame, size]}>
      <Image source={{ uri: photoUri }} style={size} resizeMode="stretch" />
      {points ? (
        <Svg
          style={StyleSheet.absoluteFill}
          viewBox="0 0 1 1"
          preserveAspectRatio="none"
        >
          <Polygon
            points={points}
            fill={colour}
            fillOpacity={0.22}
            stroke={colour}
            strokeWidth={3}
            vectorEffect="non-scaling-stroke"
            strokeLinejoin="round"
          />
        </Svg>
      ) : null}
      {label ? (
        <View style={[styles.chip, { backgroundColor: colour }]}>
          <Text style={styles.chipText} numberOfLines={1}>
            {label}
            {result?.confidence_score != null
              ? `  ${Math.round(result.confidence_score * 100)}%`
              : ""}
          </Text>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  frame: {
    borderRadius: 14,
    overflow: "hidden",
    marginBottom: 20,
    backgroundColor: "#000",
  },
  chip: {
    position: "absolute",
    left: 8,
    top: 8,
    maxWidth: "90%",
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
  },
  chipText: {
    color: "#fff",
    fontSize: 12,
    fontWeight: "bold",
  },
});
