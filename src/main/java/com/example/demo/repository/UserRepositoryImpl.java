package com.example.demo.repository;

import java.util.ArrayList;
import java.util.List;

import org.springframework.stereotype.Repository;

import com.example.demo.model.User;

@Repository
public class UserRepositoryImpl implements UserRepository {

    @Override
    public List<User> getAllUser() {
        
        User u1 = new User();
        u1.setId(1);
        u1.setName("Tom");
        
        User u2 = new User();
        u2.setId(2);
        u2.setName("Tommy");
        
        List<User> listUser = new ArrayList<>();
        listUser.add(u1);
        listUser.add(u2);
        
        return listUser;
    }

}
