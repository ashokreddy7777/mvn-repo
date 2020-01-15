package com.elizacorp.commons.serializers;

import org.apache.commons.lang3.StringUtils;
import org.junit.Assert;
import org.junit.BeforeClass;
import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;

/**
 * Tests for JsonMapper class.
 */
public class JsonMapperTest {

    private Random random = new Random();

    @BeforeClass
    public static void beforeAll() {
        new JsonMapper();
    }

    @Test
    public void testObject() throws Exception {
        JsonMapperTestHelper obj = createObj();

        String json = JsonMapper.toJson(obj);
        Assert.assertTrue(StringUtils.isNotBlank(json));

        JsonMapperTestHelper newObj = JsonMapper.toObject(json, JsonMapperTestHelper.class);
        Assert.assertNotNull(newObj);
        Assert.assertEquals(obj, newObj);
    }

    @Test
    public void testList() throws Exception {
        List<JsonMapperTestHelper> objs = new ArrayList<>();
        objs.add(createObj());
        objs.add(createObj());
        objs.add(createObj());

        String json = JsonMapper.toJson(objs);
        Assert.assertTrue(StringUtils.isNotBlank(json));

        List<JsonMapperTestHelper> newObjs = JsonMapper.toObjectList(json, JsonMapperTestHelper.class);
        Assert.assertNotNull(newObjs);
        Assert.assertEquals(objs.size(), newObjs.size());
        for (int i = 0; i < objs.size(); i++) {
            Assert.assertEquals(objs.get(i), newObjs.get(i));
        }
    }

    @Test
    public void testMap() throws Exception {
        Map<String, JsonMapperTestHelper> map = new HashMap<>();
        map.put(name(), createObj());
        map.put(name(), createObj());
        map.put(name(), createObj());

        String json = JsonMapper.toJson(map);
        Assert.assertTrue(StringUtils.isNotBlank(json));

        Map<String, JsonMapperTestHelper> newMap = JsonMapper.toObjectMap(json,
                                                                          String.class,
                                                                          JsonMapperTestHelper.class);
        Assert.assertNotNull(newMap);
        Assert.assertEquals(map.size(), newMap.size());
        map.keySet().forEach(key -> {
            Assert.assertTrue(newMap.containsKey(key));
            Assert.assertEquals(map.get(key), newMap.get(key));
        });
    }

    private JsonMapperTestHelper createObj() {
        return new JsonMapperTestHelper().withName(name())
                                         .withAge(random.nextInt(20))
                                         .withFriends(Arrays.asList(name(), name()))
                                         .withThings(new HashMap<String, String>() {{
                                             put("key1", "value1");
                                             put("key2", "value2");
                                             put("key3", "value3");
                                         }})
                                         .withModifiedDate(new Date());
    }

    private String name() {
        return "name-" + random.nextInt(50);
    }
}
