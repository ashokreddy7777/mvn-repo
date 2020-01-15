package com.elizacorp.commons.serializers;

import com.fasterxml.jackson.annotation.JsonIgnore;

import org.apache.commons.lang3.builder.EqualsBuilder;

import java.util.Date;
import java.util.List;
import java.util.Map;

/**
 * Test class for JsonMapper tests.
 */
@SuppressWarnings("unused")
class JsonMapperTestHelper {
    private String name;
    private int age;
    private List<String> friends;
    private Map<String, String> things;
    private Date modifiedDate;

    @JsonIgnore
    JsonMapperTestHelper withName(String name) {
        this.name = name;
        return this;
    }

    @JsonIgnore
    JsonMapperTestHelper withAge(int age) {
        this.age = age;
        return this;
    }

    @JsonIgnore
    JsonMapperTestHelper withFriends(List<String> friends) {
        this.friends = friends;
        return this;
    }

    @JsonIgnore
    JsonMapperTestHelper withThings(Map<String, String> things) {
        this.things = things;
        return this;
    }

    @JsonIgnore
    JsonMapperTestHelper withModifiedDate(Date modifiedDate) {
        this.modifiedDate = modifiedDate;
        return this;
    }

    public String getName() {
        return name;
    }

    public void setName(String name) {
        this.name = name;
    }

    public int getAge() {
        return age;
    }

    public void setAge(int age) {
        this.age = age;
    }

    public List<String> getFriends() {
        return friends;
    }

    public void setFriends(List<String> friends) {
        this.friends = friends;
    }

    public Map<String, String> getThings() {
        return things;
    }

    public void setThings(Map<String, String> things) {
        this.things = things;
    }

    public Date getModifiedDate() {
        return modifiedDate;
    }

    public void setModifiedDate(Date modifiedDate) {
        this.modifiedDate = modifiedDate;
    }

    @Override
    public boolean equals(Object other) {
        return (other instanceof JsonMapperTestHelper) && EqualsBuilder.reflectionEquals(this, other);
    }
}
