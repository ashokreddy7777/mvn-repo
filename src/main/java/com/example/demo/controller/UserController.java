package com.example.demo.controller;

import java.util.List;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.web.bind.annotation.CrossOrigin;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.example.demo.model.User;
import com.example.demo.service.UserServiceImpl;

@RestController
@RequestMapping("/Api")
@CrossOrigin
public class UserController {
    
    @Autowired
    private UserServiceImpl service;
    
    @GetMapping("User")
    public List<User> getAllUsers() {
        return service.getAllUser();  
    }

}
