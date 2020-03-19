pipeline {
        agent none
        stages {
            stage("Git Checkout"){
            agent any
            steps {
                    checkout([$class: 'GitSCM', branches: [[name: '*/master']], doGenerateSubmoduleConfigurations: false, extensions: [], submoduleCfg: [], userRemoteConfigs: [[url: 'https://github.com/ashokreddy7777/Test.git']]])
                }
            }
            stage("build & SonarQube analysis") {
            agent any
            steps {
              withSonarQubeEnv('sonarqube') {
                bat 'mvn clean package sonar:sonar'
              }
            }
          }
            stage("veracodescan"){
        steps{
            veracode applicationName:'Eliza', 
            canFailJob: true,
            createSandbox: true, 
            criticality: 'VeryHigh', 
            debug: true,
            fileNamePattern: '',
            replacementPattern: '', 
            sandboxName: 'sonar-veracode', 
            scanExcludesPattern: '', 
            scanIncludesPattern: '', 
            scanName: '$buildnumber', 
            teams: '', 
            timeout: 60, 
            uploadExcludesPattern: '', 
            uploadIncludesPattern: '**/**.jar',
            vid: 'ff9dc6d3ddf16b9df17aed9814dca85a',
            vkey: '2150d0dbaca348dbc7acf6884287c93aa6d7916afd3f5b9d2bd7679765c40438d472523816100fe767fb60399767b0403cada929908572dd89532b4d48a45e96', 
            waitForScan: true
        }
    }
    }
    }
