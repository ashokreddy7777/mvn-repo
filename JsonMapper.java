package com.elizacorp.commons.serializers;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.SerializationFeature;

import java.io.IOException;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Utilities to convert Objects to JSON and back again.
 */
@SuppressWarnings("WeakerAccess")
public class JsonMapper {
    private static ObjectMapper mapper =
            new ObjectMapper().disable(SerializationFeature.FAIL_ON_EMPTY_BEANS)
                              .disable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS)
                              .enable(SerializationFeature.WRITE_DATES_WITH_ZONE_ID)
                              .disable(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES)
                              .enable(DeserializationFeature.ACCEPT_EMPTY_STRING_AS_NULL_OBJECT);

    /**
     * Serialize an object into a string in JSON format.
     *
     * @param obj Object to convert.
     * @return String in JSON format
     * @throws JsonProcessingException JSON processing error.
     */
    public static String toJson(Object obj) throws JsonProcessingException {
        return mapper.writeValueAsString(obj);
    }

    /**
     * Take a string in JSON format and deserialize it into an object.
     *
     * @param json        JSON string.
     * @param targetClass Target class type.
     * @param <T>         Target class type.
     * @return An object of type T.
     * @throws IOException Processing error.
     */
    public static <T> T toObject(String json, Class<T> targetClass) throws IOException {
        return mapper.readValue(json, targetClass);
    }

    /**
     * Take a string in JSON format and deserialize it into a list of objects.
     *
     * @param json        JSON string.
     * @param targetClass Target class type.
     * @param <T>         Target class type.
     * @return A list of type T.
     * @throws IOException Processing error.
     */
    public static <T> List<T> toObjectList(String json, Class<T> targetClass) throws IOException {
        List<LinkedHashMap> values =
                mapper.readValue(json, new TypeReference<List<LinkedHashMap>>() {});

        List<T> results = new ArrayList<>(values.size());
        values.forEach(value -> results.add(mapper.convertValue(value, targetClass)));
        return results;
    }

    /**
     * Take a string in JSON format and deserialize it into a map of objects.
     *
     * @param json       JSON string.
     * @param keyClass   Key class type.
     * @param valueClass Value class type.
     * @param <K>        Key class type.
     * @param <V>        Value class type.
     * @return A map of K,V
     * @throws IOException Processing error.
     */
    public static <K, V> Map<K, V> toObjectMap(String json, Class<K> keyClass, Class<V> valueClass) throws IOException {
        Map<K, LinkedHashMap> values =
                mapper.readValue(json, new TypeReference<Map<K, LinkedHashMap>>() {});

        Map<K, V> results = new HashMap<>(values.size());
        values.keySet()
              .forEach(key -> results.put(mapper.convertValue(key, keyClass),
                                          mapper.convertValue(values.get(key), valueClass)));
        return results;
    }
}
